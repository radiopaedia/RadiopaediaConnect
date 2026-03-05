/**
 * Custom DICOM image loader for legacy cornerstone-core.
 *
 * Fetches raw DICOM bytes from the backend (no server-side transcoding),
 * parses headers with dicom-parser, extracts JPEG frames via byte-scanning,
 * and decodes with jpeg-lossless-decoder-js for JPEG Lossless transfer syntaxes.
 *
 * Image ID format: csdicom:{seriesUid}|{fileName}|{frameIndex}
 */
import * as cornerstone from 'cornerstone-core';
import dicomParser from 'dicom-parser';

// Transfer syntax UIDs
const TS_IMPLICIT_VR_LE = '1.2.840.10008.1.2';
const TS_EXPLICIT_VR_LE = '1.2.840.10008.1.2.1';
const TS_DEFLATED_EXPLICIT_VR_LE = '1.2.840.10008.1.2.1.99';
const TS_EXPLICIT_VR_BE = '1.2.840.10008.1.2.2';
const TS_JPEG_BASELINE = '1.2.840.10008.1.2.4.50';
const TS_JPEG_EXTENDED = '1.2.840.10008.1.2.4.51';
const TS_JPEG_LOSSLESS_P14 = '1.2.840.10008.1.2.4.57';
const TS_JPEG_LOSSLESS_P14_SV1 = '1.2.840.10008.1.2.4.70';


// ── File-level cache & fetch deduplication ──

/** @type {Map<string, {arrayBuffer: ArrayBuffer, dataSet: object, jpegFrames: Uint8Array[]|null}>} */
const fileCache = new Map();

/** @type {Map<string, Promise<{arrayBuffer: ArrayBuffer, dataSet: object, jpegFrames: Uint8Array[]|null}>>} */
const pendingFetches = new Map();

/** Lazy-loaded JPEG Lossless decoder class */
let JpegLosslessDecoderClass = null;

// ── Public API ──

let isRegistered = false;

export function registerCsdicomLoader() {
  if (isRegistered) return;
  cornerstone.registerImageLoader('csdicom', (imageId) => ({
    promise: loadImage(imageId),
  }));
  isRegistered = true;
}

export function clearFileCache() {
  fileCache.clear();
  pendingFetches.clear();
}

// ── Image ID parsing ──

function parseImageId(imageId) {
  const afterScheme = imageId.slice('csdicom:'.length);
  const parts = afterScheme.split('|');
  return {
    seriesUid: parts[0],
    fileName: parts[1],
    frameIndex: parseInt(parts[2], 10) || 0,
  };
}

// ── Fetch + parse with caching ──

async function getParsedFile(seriesUid, fileName) {
  const cacheKey = `${seriesUid}/${fileName}`;

  if (fileCache.has(cacheKey)) {
    return fileCache.get(cacheKey);
  }

  if (pendingFetches.has(cacheKey)) {
    return pendingFetches.get(cacheKey);
  }

  const fetchPromise = (async () => {
    const response = await fetch(
      `/api/cornerstone/raw?seriesUid=${encodeURIComponent(seriesUid)}&filename=${encodeURIComponent(fileName)}`,
    );
    if (!response.ok) throw new Error(`Failed to fetch DICOM: ${response.status} ${response.statusText}`);

    const arrayBuffer = await response.arrayBuffer();
    const byteArray = new Uint8Array(arrayBuffer);
    const dataSet = dicomParser.parseDicom(byteArray);

    const transferSyntax = dataSet.string('x00020010') || TS_IMPLICIT_VR_LE;
    let jpegFrames = null;

    // For encapsulated transfer syntaxes, extract all JPEG frames upfront
    if (isEncapsulated(transferSyntax)) {
      const pixelDataElement = dataSet.elements['x7fe00010'];
      if (pixelDataElement) {
        jpegFrames = extractAllJpegFrames(byteArray, pixelDataElement.dataOffset);
      }
    }

    const result = { arrayBuffer, dataSet, jpegFrames };
    fileCache.set(cacheKey, result);
    pendingFetches.delete(cacheKey);
    return result;
  })();

  pendingFetches.set(cacheKey, fetchPromise);

  // If fetch fails, clean up so retries can occur
  fetchPromise.catch(() => {
    pendingFetches.delete(cacheKey);
  });

  return fetchPromise;
}

// ── JPEG frame extraction via byte scanning ──

/**
 * Scans raw bytes for JPEG SOI (FF D8 FF) and EOI (FF D9) markers.
 * This handles Siemens encapsulation quirks, empty offset tables, and
 * non-standard item boundaries that break dicomParser's fragment reader.
 */
function extractAllJpegFrames(byteArray, hintOffset) {
  const frames = [];
  let searchFrom = hintOffset;

  while (searchFrom < byteArray.length - 2) {
    // Find next SOI: FF D8 FF
    let soiPos = -1;
    for (let i = searchFrom; i < byteArray.length - 2; i++) {
      if (byteArray[i] === 0xff && byteArray[i + 1] === 0xd8 && byteArray[i + 2] === 0xff) {
        soiPos = i;
        break;
      }
    }
    if (soiPos < 0) break;

    // Find EOI: FF D9
    let eoiPos = -1;
    for (let j = soiPos + 4; j < byteArray.length - 1; j++) {
      if (byteArray[j] === 0xff && byteArray[j + 1] === 0xd9) {
        eoiPos = j;
        break;
      }
    }

    if (eoiPos < 0) {
      // No EOI found — take everything from SOI to end
      frames.push(byteArray.slice(soiPos));
      break;
    }

    frames.push(byteArray.slice(soiPos, eoiPos + 2));
    searchFrom = eoiPos + 2;
  }
  return frames;
}

// ── Transfer syntax helpers ──

function isEncapsulated(ts) {
  return (
    ts !== TS_IMPLICIT_VR_LE &&
    ts !== TS_EXPLICIT_VR_LE &&
    ts !== TS_DEFLATED_EXPLICIT_VR_LE &&
    ts !== TS_EXPLICIT_VR_BE
  );
}

function isJpegLossless(ts) {
  return ts === TS_JPEG_LOSSLESS_P14 || ts === TS_JPEG_LOSSLESS_P14_SV1;
}

function isJpegLossy(ts) {
  return ts === TS_JPEG_BASELINE || ts === TS_JPEG_EXTENDED;
}

// ── JPEG Lossless decoder (lazy-loaded) ──

async function getJpegLosslessDecoder() {
  if (!JpegLosslessDecoderClass) {
    const mod = await import('jpeg-lossless-decoder-js');
    // The module may export Decoder directly or as a named export
    JpegLosslessDecoderClass = mod.Decoder || mod.default?.Decoder || mod.default;
  }
  return JpegLosslessDecoderClass;
}

// ── Pixel data extraction ──

function extractUncompressedFrame(dataSet, frameIndex) {
  const pixelDataElement = dataSet.elements['x7fe00010'];
  if (!pixelDataElement) throw new Error('No pixel data element (7FE0,0010)');

  const bitsAllocated = dataSet.uint16('x00280100') || 16;
  const rows = dataSet.uint16('x00280010');
  const cols = dataSet.uint16('x00280011');
  const samplesPerPixel = dataSet.uint16('x00280002') || 1;
  const bytesPerPixel = bitsAllocated / 8;
  const frameSize = rows * cols * samplesPerPixel * bytesPerPixel;
  const offset = pixelDataElement.dataOffset + frameIndex * frameSize;

  return new Uint8Array(dataSet.byteArray.buffer, dataSet.byteArray.byteOffset + offset, frameSize);
}

// ── Browser-native JPEG decoding (for lossy JPEG) ──

async function decodeJpegWithBrowser(jpegBytes) {
  const blob = new Blob([jpegBytes], { type: 'image/jpeg' });
  const bitmap = await createImageBitmap(blob);

  let canvas;
  let ctx;
  if (typeof OffscreenCanvas !== 'undefined') {
    canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    ctx = canvas.getContext('2d');
  } else {
    canvas = document.createElement('canvas');
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    ctx = canvas.getContext('2d');
  }

  ctx.drawImage(bitmap, 0, 0);
  const imageData = ctx.getImageData(0, 0, bitmap.width, bitmap.height);
  bitmap.close();
  return imageData.data; // Uint8ClampedArray RGBA
}

// ── Main loader ──

async function loadImage(imageId) {
  const { seriesUid, fileName, frameIndex } = parseImageId(imageId);
  const { dataSet, jpegFrames } = await getParsedFile(seriesUid, fileName);

  const transferSyntax = dataSet.string('x00020010') || TS_IMPLICIT_VR_LE;
  const rows = dataSet.uint16('x00280010');
  const cols = dataSet.uint16('x00280011');
  const bitsAllocated = dataSet.uint16('x00280100') || 16;
  const pixelRepresentation = dataSet.uint16('x00280103') || 0;
  const samplesPerPixel = dataSet.uint16('x00280002') || 1;
  const photometric = dataSet.string('x00280004') || 'MONOCHROME2';
  const isColor = samplesPerPixel > 1 || photometric === 'RGB' || photometric === 'YBR_FULL';

  let typedPixelData;
  let renderRgba = null; // For browser-decoded lossy JPEG (RGBA data)

  if (!isEncapsulated(transferSyntax)) {
    // Uncompressed: read raw pixel bytes
    const rawFrame = extractUncompressedFrame(dataSet, frameIndex);
    typedPixelData = createTypedArray(rawFrame, bitsAllocated, pixelRepresentation, isColor);
  } else if (isJpegLossless(transferSyntax)) {
    // JPEG Lossless: decode with jpeg-lossless-decoder-js
    if (!jpegFrames || frameIndex >= jpegFrames.length) {
      throw new Error(`JPEG frame ${frameIndex} not found (have ${jpegFrames?.length || 0} frames)`);
    }
    const frameBytes = jpegFrames[frameIndex];
    const DecoderClass = await getJpegLosslessDecoder();
    const decoder = new DecoderClass();
    const resultBuffer = decoder.decompress(frameBytes.buffer, frameBytes.byteOffset, frameBytes.byteLength);
    typedPixelData = createTypedArrayFromDecoded(resultBuffer, bitsAllocated, pixelRepresentation);
  } else if (isJpegLossy(transferSyntax)) {
    // Lossy JPEG: decode with browser's native decoder
    if (!jpegFrames || frameIndex >= jpegFrames.length) {
      throw new Error(`JPEG frame ${frameIndex} not found (have ${jpegFrames?.length || 0} frames)`);
    }
    const rgbaData = await decodeJpegWithBrowser(jpegFrames[frameIndex]);
    renderRgba = rgbaData;
    // Extract just the RGB channels for cornerstone (ignore alpha)
    typedPixelData = new Uint8Array(rows * cols * 3);
    for (let i = 0; i < rows * cols; i++) {
      typedPixelData[i * 3] = rgbaData[i * 4];
      typedPixelData[i * 3 + 1] = rgbaData[i * 4 + 1];
      typedPixelData[i * 3 + 2] = rgbaData[i * 4 + 2];
    }
  } else {
    throw new Error(`Unsupported transfer syntax: ${transferSyntax}`);
  }

  // Read DICOM tags for display
  const slope = parseFloat(dataSet.string('x00281053')) || 1;
  const intercept = parseFloat(dataSet.string('x00281052')) || 0;

  // Parse WC/WW (may be multi-valued strings like "400\\40")
  const wcStr = dataSet.string('x00281050');
  const wwStr = dataSet.string('x00281051');
  let windowCenter = wcStr ? parseFloat(wcStr.split('\\')[0]) : undefined;
  let windowWidth = wwStr ? parseFloat(wwStr.split('\\')[0]) : undefined;

  // Pixel spacing (may be stored as "rowSpacing\\colSpacing")
  const spacingStr = dataSet.string('x00280030');
  let rowPixelSpacing = 1;
  let columnPixelSpacing = 1;
  if (spacingStr) {
    const parts = spacingStr.split('\\');
    rowPixelSpacing = parseFloat(parts[0]) || 1;
    columnPixelSpacing = parseFloat(parts[1]) || rowPixelSpacing;
  }

  // Compute min/max from actual pixel values (for grayscale)
  let minPixelValue = 0;
  let maxPixelValue = 255;

  if (!isColor && !renderRgba) {
    minPixelValue = Infinity;
    maxPixelValue = -Infinity;
    for (let i = 0; i < typedPixelData.length; i++) {
      const v = typedPixelData[i];
      if (v < minPixelValue) minPixelValue = v;
      if (v > maxPixelValue) maxPixelValue = v;
    }
  }

  // Compute WC/WW from actual pixel range if not in DICOM tags
  if (windowCenter === undefined || windowWidth === undefined || isNaN(windowCenter) || isNaN(windowWidth)) {
    windowCenter = (minPixelValue + maxPixelValue) / 2;
    windowWidth = maxPixelValue - minPixelValue || 1;
  }

  const invert = photometric === 'MONOCHROME1';

  // Build the legacy cornerstone image object
  const image = {
    imageId,
    minPixelValue,
    maxPixelValue,
    slope,
    intercept,
    windowCenter,
    windowWidth,
    rows,
    columns: cols,
    height: rows,
    width: cols,
    color: isColor || !!renderRgba,
    rgba: !!renderRgba,
    columnPixelSpacing,
    rowPixelSpacing,
    sizeInBytes: typedPixelData.byteLength,
    invert,
    getPixelData: () => typedPixelData,
    render: renderRgba ? createRgbaRenderer(renderRgba, rows, cols) : undefined,
  };

  return image;
}

// ── TypedArray helpers ──

function createTypedArray(rawBytes, bitsAllocated, pixelRepresentation, isColor) {
  if (isColor || bitsAllocated <= 8) {
    return new Uint8Array(rawBytes.buffer, rawBytes.byteOffset, rawBytes.byteLength);
  }
  if (pixelRepresentation === 1) {
    return new Int16Array(rawBytes.buffer, rawBytes.byteOffset, rawBytes.byteLength / 2);
  }
  return new Uint16Array(rawBytes.buffer, rawBytes.byteOffset, rawBytes.byteLength / 2);
}

function createTypedArrayFromDecoded(resultBuffer, bitsAllocated, pixelRepresentation) {
  if (bitsAllocated <= 8) {
    return new Uint8Array(resultBuffer);
  }
  if (pixelRepresentation === 1) {
    return new Int16Array(resultBuffer, 0, resultBuffer.byteLength / 2);
  }
  return new Uint16Array(resultBuffer, 0, resultBuffer.byteLength / 2);
}

// ── RGBA renderer for browser-decoded JPEG ──

/**
 * Creates a custom render function for images decoded via the browser's JPEG decoder.
 * The browser gives us RGBA data which we render directly to canvas.
 */
function createRgbaRenderer(rgbaData, rows, cols) {
  return function (enabledElement, invalidated) {
    if (invalidated) {
      const canvas = enabledElement.canvas;
      const ctx = canvas.getContext('2d');
      const imageData = new ImageData(new Uint8ClampedArray(rgbaData.buffer), cols, rows);
      ctx.putImageData(imageData, 0, 0);
    }
  };
}
