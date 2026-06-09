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
  const planarConfiguration = dataSet.uint16('x00280006') || 0;

  if (!rows || !cols) {
    throw new Error(`Missing image dimensions (Rows=${rows}, Columns=${cols})`);
  }
  const px = rows * cols;

  const isPalette = photometric === 'PALETTE COLOR';
  const isSubsampled = photometric.indexOf('422') !== -1 || photometric.indexOf('420') !== -1;
  const hasColorSamples = samplesPerPixel >= 3;

  let typedPixelData; // final array returned by getPixelData()
  let producedRgba = false; // true once typedPixelData holds 4-byte RGBA
  let colorSamples = null; // interleaved/planar 3-byte (RGB or YBR_FULL) samples
  let samplesArePlanar = false;
  let grayscale = null; // grayscale or palette-index values

  // ── 1. Obtain pixel samples according to the transfer syntax ──
  if (!isEncapsulated(transferSyntax)) {
    if (transferSyntax === TS_DEFLATED_EXPLICIT_VR_LE) {
      throw new Error('Deflated Explicit VR Little Endian pixel data is not supported');
    }
    const rawFrame = extractUncompressedFrame(dataSet, frameIndex);
    if (hasColorSamples) {
      assertSupportedColor(samplesPerPixel, bitsAllocated, isSubsampled, photometric);
      colorSamples = rawFrame;
      samplesArePlanar = planarConfiguration === 1;
    } else {
      grayscale = createTypedArray(rawFrame, bitsAllocated, pixelRepresentation, false);
      if (transferSyntax === TS_EXPLICIT_VR_BE && bitsAllocated > 8) {
        grayscale = byteSwap16Copy(grayscale); // big-endian → host order
      }
    }
  } else if (isJpegLossless(transferSyntax)) {
    const resultBuffer = await decodeJpegLosslessFrame(jpegFrames, frameIndex);
    if (hasColorSamples) {
      assertSupportedColor(samplesPerPixel, bitsAllocated, isSubsampled, photometric);
      colorSamples = new Uint8Array(resultBuffer); // decoder output is interleaved
      samplesArePlanar = false;
    } else {
      grayscale = createTypedArrayFromDecoded(resultBuffer, bitsAllocated, pixelRepresentation);
    }
  } else if (isJpegLossy(transferSyntax)) {
    if (!jpegFrames || frameIndex >= jpegFrames.length) {
      throw new Error(`JPEG frame ${frameIndex} not found (have ${jpegFrames?.length || 0} frames)`);
    }
    // The browser decoder applies any YBR→RGB transform and yields RGBA directly.
    const rgbaData = await decodeJpegWithBrowser(jpegFrames[frameIndex]);
    typedPixelData = new Uint8Array(rgbaData.buffer, rgbaData.byteOffset, rgbaData.byteLength);
    producedRgba = true;
  } else {
    throw new Error(
      `Unsupported transfer syntax: ${transferSyntax}. JPEG-LS, JPEG 2000 and RLE ` +
        `cannot be decoded in-browser — transcode server-side first.`,
    );
  }

  // ── 2. Normalise to what cornerstone expects (RGBA for colour, raw for gray) ──
  if (!producedRgba) {
    if (hasColorSamples) {
      // Legacy cornerstone renders colour via getPixelData() with a 4-byte RGBA
      // stride (storedColorPixelDataToCanvasImageData / storedRGBA...). Convert the
      // 3-byte samples (and YBR_FULL → RGB) here, honouring PlanarConfiguration.
      // Without this, RGB result/reformat series (Siemens perfusion maps: RELCBV,
      // RELMTT, TTP, overlays) render as garbled rainbow noise from the stride.
      if (colorSamples.length < px * 3) {
        throw new Error(`Truncated colour pixel data: ${colorSamples.length} bytes, need ${px * 3}`);
      }
      typedPixelData = colorSamplesToRgba(colorSamples, px, photometric, samplesArePlanar);
      producedRgba = true;
    } else if (isPalette) {
      typedPixelData = paletteToRgba(dataSet, grayscale, px);
      producedRgba = true;
    } else {
      if (!grayscale || grayscale.length < px) {
        throw new Error(`Truncated grayscale pixel data: ${grayscale?.length || 0}, need ${px}`);
      }
      typedPixelData = grayscale;
    }
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

  if (!producedRgba) {
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

  const invert = !producedRgba && photometric === 'MONOCHROME1';

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
    color: producedRgba,
    rgba: producedRgba,
    columnPixelSpacing,
    rowPixelSpacing,
    sizeInBytes: typedPixelData.byteLength,
    invert,
    getPixelData: () => typedPixelData,
  };

  return image;
}

// ── Colour / decoding helpers ──

/** Rejects colour formats we cannot faithfully render client-side. */
function assertSupportedColor(samplesPerPixel, bitsAllocated, isSubsampled, photometric) {
  if (samplesPerPixel !== 3 || bitsAllocated !== 8) {
    throw new Error(
      `Unsupported colour format: SamplesPerPixel=${samplesPerPixel}, ` +
        `BitsAllocated=${bitsAllocated} (only 8-bit, 3-sample colour is supported)`,
    );
  }
  if (isSubsampled) {
    throw new Error(`Chroma-subsampled ${photometric} requires server-side decoding`);
  }
  if (photometric !== 'RGB' && photometric !== 'YBR_FULL') {
    throw new Error(`Unsupported colour photometric interpretation: ${photometric}`);
  }
}

async function decodeJpegLosslessFrame(jpegFrames, frameIndex) {
  if (!jpegFrames || frameIndex >= jpegFrames.length) {
    throw new Error(`JPEG frame ${frameIndex} not found (have ${jpegFrames?.length || 0} frames)`);
  }
  const frameBytes = jpegFrames[frameIndex];
  const DecoderClass = await getJpegLosslessDecoder();
  const decoder = new DecoderClass();
  return decoder.decompress(frameBytes.buffer, frameBytes.byteOffset, frameBytes.byteLength);
}

function clamp8(v) {
  return v < 0 ? 0 : v > 255 ? 255 : v | 0;
}

/** Byte-swaps 16-bit samples (big-endian → host) into a fresh array (no cache mutation). */
function byteSwap16Copy(arr) {
  const src = new Uint8Array(arr.buffer, arr.byteOffset, arr.byteLength);
  const out = new Uint8Array(src.length);
  for (let i = 0; i + 1 < src.length; i += 2) {
    out[i] = src[i + 1];
    out[i + 1] = src[i];
  }
  return arr instanceof Int16Array ? new Int16Array(out.buffer) : new Uint16Array(out.buffer);
}

/** Converts 3-byte colour samples (RGB or YBR_FULL, interleaved or planar) to 4-byte RGBA. */
function colorSamplesToRgba(samples, px, photometric, isPlanar) {
  const rgba = new Uint8Array(px * 4);
  const ybr = photometric === 'YBR_FULL';
  for (let i = 0; i < px; i++) {
    let c0;
    let c1;
    let c2;
    if (isPlanar) {
      c0 = samples[i];
      c1 = samples[px + i];
      c2 = samples[2 * px + i];
    } else {
      c0 = samples[i * 3];
      c1 = samples[i * 3 + 1];
      c2 = samples[i * 3 + 2];
    }
    let r;
    let g;
    let b;
    if (ybr) {
      // YBR_FULL → RGB (DICOM PS3.3 C.7.6.3.1.2)
      const cb = c1 - 128;
      const cr = c2 - 128;
      r = c0 + 1.402 * cr;
      g = c0 - 0.344136 * cb - 0.714136 * cr;
      b = c0 + 1.772 * cb;
    } else {
      r = c0;
      g = c1;
      b = c2;
    }
    rgba[i * 4] = clamp8(r);
    rgba[i * 4 + 1] = clamp8(g);
    rgba[i * 4 + 2] = clamp8(b);
    rgba[i * 4 + 3] = 255;
  }
  return rgba;
}

/** Maps PALETTE COLOR index values through the per-channel LUTs to 4-byte RGBA. */
function paletteToRgba(dataSet, indices, px) {
  if (!indices) throw new Error('PALETTE COLOR image has no index data');
  const red = readPaletteChannel(dataSet, 'x00281101', 'x00281201');
  const green = readPaletteChannel(dataSet, 'x00281102', 'x00281202');
  const blue = readPaletteChannel(dataSet, 'x00281103', 'x00281203');
  if (!red || !green || !blue) {
    throw new Error('PALETTE COLOR image is missing palette LUT data');
  }
  const rgba = new Uint8Array(px * 4);
  for (let i = 0; i < px; i++) {
    const v = indices[i];
    rgba[i * 4] = paletteLookup(red, v);
    rgba[i * 4 + 1] = paletteLookup(green, v);
    rgba[i * 4 + 2] = paletteLookup(blue, v);
    rgba[i * 4 + 3] = 255;
  }
  return rgba;
}

function readPaletteChannel(dataSet, descTag, dataTag) {
  const el = dataSet.elements[dataTag];
  if (!el) return null;
  let numEntries = dataSet.uint16(descTag, 0);
  if (numEntries === 0) numEntries = 65536; // per DICOM, 0 means 2^16
  const firstMapped = dataSet.uint16(descTag, 1) || 0;
  const bitsPerEntry = dataSet.uint16(descTag, 2) || 16;
  const dv = new DataView(dataSet.byteArray.buffer, dataSet.byteArray.byteOffset + el.dataOffset, el.length);
  const lut = new Uint8Array(numEntries);
  if (bitsPerEntry === 8) {
    for (let i = 0; i < numEntries && i < el.length; i++) lut[i] = dv.getUint8(i);
  } else {
    const count = Math.min(numEntries, Math.floor(el.length / 2));
    for (let i = 0; i < count; i++) lut[i] = dv.getUint16(i * 2, true) >> 8; // 16-bit → high byte
  }
  return { firstMapped, numEntries, lut };
}

function paletteLookup(channel, value) {
  let idx = value - channel.firstMapped;
  if (idx < 0) idx = 0;
  else if (idx >= channel.numEntries) idx = channel.numEntries - 1;
  return channel.lut[idx];
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
