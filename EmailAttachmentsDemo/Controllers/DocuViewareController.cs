using EmailAttachmentsDemo.Models;
using GdPicture14.WEB;
using Microsoft.AspNetCore.Mvc;

namespace EmailAttachmentsDemo.Controllers
{
    [ApiController]
    [Route("api/DocuVieware")]
    public class DocuViewareController : ControllerBase
    {
        private const int SessionTimeoutMinutes = 20;

        /// <summary>
        /// Returns the control markup for the session. The toolbar is set server side:
        /// files are opened with the "Load and merge" button, and saved with the
        /// "Save on server" button.
        /// </summary>
        [HttpPost]
        [Route("GetDocuViewareControl")]
        public DocuViewareRESTOutputResponse GetDocuViewareControl(DocuViewareConfiguration controlConfiguration)
        {
            if (!DocuViewareManager.IsSessionAlive(controlConfiguration.SessionId))
            {
                if (string.IsNullOrEmpty(controlConfiguration.SessionId) || string.IsNullOrEmpty(controlConfiguration.ControlId))
                {
                    throw new Exception("Invalid session identifier and/or invalid control identifier.");
                }
                DocuViewareManager.CreateDocuViewareSession(controlConfiguration.SessionId, controlConfiguration.ControlId, SessionTimeoutMinutes);
            }

            using (DocuViewareControl docuVieware = new DocuViewareControl(controlConfiguration.SessionId))
            {
                docuVieware.ShowTextSearchSnapIn = false;
                docuVieware.ShowAnnotationsSnapIn = false;
                docuVieware.ShowThumbnailsSnapIn = true;
                docuVieware.ShowDigitalSignatureSnapIn = false;
                docuVieware.ShowRedactionSnapIn = false;
                docuVieware.ShowAnnotationsCommentsSnapIn = false;
                docuVieware.AllowUpload = false;
                docuVieware.AllowPrint = false;
                docuVieware.EnableMouseModeButtons = false;
                docuVieware.EnableMarqueeZoomModeButton = false;
                docuVieware.MaxUploadSize = 36700160; // 35 MB
                // ShowLogo = false only takes effect with a production key.
                docuVieware.ShowLogo = false;
                // These buttons can only be hidden server side.
                docuVieware.EnableLoadFromUriButton = false;
                docuVieware.EnableSaveButton = false;
                // Set to true to scan with TWAIN (needs the DocuVieware TWAIN service, Windows only).
                docuVieware.EnableTwainAcquisitionButton = controlConfiguration.EnableTwainAcquisitionButton;

                docuVieware.AddCustomToolbarButton(
                    "MULTI-LOAD",
                    "Load and merge documents",
                    "<svg width='100%' height='100%' viewBox='0 0 20 20'>" +
                    "<path d='M14.613,10c0,0.23-0.188,0.419-0.419,0.419H10.42v3.774c0,0.23-0.189,0.42-0.42,0.42s-0.419-0.189-0.419-0.42v-3.774H5.806c-0.23,0-0.419-0.189-0.419-0.419s0.189-0.419,0.419-0.419h3.775V5.806c0-0.23,0.189-0.419,0.419-0.419s0.42,0.189,0.42,0.419v3.775h3.774C14.425,9.581,14.613,9.77,14.613,10 M17.969,10c0,4.401-3.567,7.969-7.969,7.969c-4.402,0-7.969-3.567-7.969-7.969c0-4.402,3.567-7.969,7.969-7.969C14.401,2.031,17.969,5.598,17.969,10 M17.13,10c0-3.932-3.198-7.13-7.13-7.13S2.87,6.068,2.87,10c0,3.933,3.198,7.13,7.13,7.13S17.13,13.933,17.13,10'></path>" +
                    "</svg>",
                    "multiloading();");
                docuVieware.AddCustomToolbarButton(
                    "CUSTOM-SAVE",
                    "Save on server",
                    "<svg width='100%' height='100%' viewBox='0 0 20 20'>" +
                    "<path d='M17.064,4.656l-2.05-2.035C14.936,2.544,14.831,2.5,14.721,2.5H3.854c-0.229,0-0.417,0.188-0.417,0.417v14.167c0,0.229,0.188,0.417,0.417,0.417h12.917c0.229,0,0.416-0.188,0.416-0.417V4.952C17.188,4.84,17.144,4.733,17.064,4.656M6.354,3.333h7.917V10H6.354V3.333z M16.354,16.667H4.271V3.333h1.25v7.083c0,0.229,0.188,0.417,0.417,0.417h8.75c0.229,0,0.416-0.188,0.416-0.417V3.886l1.25,1.239V16.667z M13.402,4.688v3.958c0,0.229-0.186,0.417-0.417,0.417c-0.229,0-0.417-0.188-0.417-0.417V4.688c0-0.229,0.188-0.417,0.417-0.417C13.217,4.271,13.402,4.458,13.402,4.688'></path>" +
                    "</svg>",
                    "customsave();");

                using (StringWriter controlOutput = new StringWriter())
                {
                    docuVieware.RenderControl(controlOutput);
                    return new DocuViewareRESTOutputResponse { HtmlContent = controlOutput.ToString() };
                }
            }
        }
    }
}
