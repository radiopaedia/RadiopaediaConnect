using FellowOakDicom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Services.Dicom;

namespace RadiopaediaConnect.Controllers
{
    /// <summary>
    /// Exposes the DICOM anonymisation policy so the UI can describe exactly what the
    /// anonymiser does, from the same in-memory source the pipeline uses. No second
    /// hand-maintained copy of the tag list in the frontend.
    /// </summary>
    [ApiController]
    [Route("api/anonymisation")]
    [AllowAnonymous]
    public class AnonymisationController : ControllerBase
    {
        private readonly DicomAllowlist _allowlist;

        public AnonymisationController(DicomAllowlist allowlist) => _allowlist = allowlist;

        /// <summary>
        /// The full anonymisation policy: tags kept verbatim (the allowlist) and the type-2
        /// PHI tags written as empty strings. "Always removed" categories are conceptual and
        /// remain described in the UI.
        /// </summary>
        [HttpGet("policy")]
        public IActionResult GetPolicy()
        {
            var keep = _allowlist.Entries.Select(e => new
            {
                tag = FormatTag(e.Tag),
                group = e.Tag.Length >= 4 ? e.Tag[..4] : "",
                alias = e.Alias,
                description = e.Description,
            });

            var zeroed = Services.Dicom.DicomAnonymizer.EmptyReplaceTags.Select(t => new
            {
                tag = $"({t.Group:X4},{t.Element:X4})",
                name = DicomDictionary.Default[t]?.Name ?? t.ToString(),
            });

            // Equipment tags overwritten with the literal "REMOVED" rather than emptied
            var removed = Services.Dicom.DicomAnonymizer.RemovedReplaceTags.Select(t => new
            {
                tag = $"({t.Group:X4},{t.Element:X4})",
                name = DicomDictionary.Default[t]?.Name ?? t.ToString(),
            });

            // Manufacturer is emptied or set to "REMOVED" depending on the SOP class — it is
            // type 2 in the General Equipment module but type 1 in Enhanced General Equipment.
            var manufacturer = DicomTag.Manufacturer;
            var conditional = new[]
            {
                new
                {
                    tag = $"({manufacturer.Group:X4},{manufacturer.Element:X4})",
                    name = DicomDictionary.Default[manufacturer]?.Name ?? manufacturer.ToString(),
                    note = "Empty for General Equipment SOP classes (CT, MR, US, DX, XA, NM, PET), "
                         + "otherwise \"REMOVED\"",
                    emptyForSopClasses = Services.Dicom.DicomAnonymizer.BlankManufacturerSopClasses,
                },
            };

            return Ok(new { keep, zeroed, removed, conditional });
        }

        /// <summary>"00181048" → "(0018,1048)".</summary>
        private static string FormatTag(string raw) =>
            raw.Length == 8 ? $"({raw[..4]},{raw[4..]})" : raw;
    }
}
