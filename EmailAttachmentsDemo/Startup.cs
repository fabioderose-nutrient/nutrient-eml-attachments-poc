using System.Text.Json;
using GdPicture14;
using GdPicture14.WEB;
using Microsoft.Extensions.FileProviders;

namespace EmailAttachmentsDemo
{
    public class Startup
    {
        /// <summary>
        /// Public base URL of the app (APP_URI, default http://localhost:62500). DocuVieware
        /// embeds it in the control markup, so the browser must be able to reach it.
        /// </summary>
        public static string AppUri =>
            Environment.GetEnvironmentVariable("APP_URI") is { Length: > 0 } uri ? uri.TrimEnd('/') : "http://localhost:62500";

        private const string MergedFileName = "merged-document.pdf";
        private const string SavedDocumentsRequestPath = "/saved-documents";

        /// <summary>Folder where "Save on server" writes documents (served at /saved-documents).</summary>
        private static readonly string SavedDocumentsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "SavedDocuments");

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            // License key from DOCUVIEWARE_LICENSE_KEY. An empty key starts evaluation mode
            // (RegisterKEY must still be called).
            DocuViewareLicensing.RegisterKEY(Environment.GetEnvironmentVariable("DOCUVIEWARE_LICENSE_KEY") ?? "");
            DocuViewareManager.SetupConfiguration(true, DocuViewareSessionStateMode.File,
                Path.Combine(Directory.GetCurrentDirectory(), "Cache"), AppUri, "api/docuvieware3");
            DocuViewareEventsHandler.CustomAction += OnCustomAction;
        }

        public IConfiguration Configuration { get; }

        // =====================================================================
        //  Custom actions posted by wwwroot/index.html with
        //  DocuViewareAPI.PostCustomServerAction
        // =====================================================================
        private static void OnCustomAction(object sender, CustomActionEventArgs e)
        {
            switch (e.actionName)
            {
                case "GetAttachmentsForLoadedDocument":
                    GetAttachments(e);
                    break;
                case "LoadSingleFile":
                    LoadSingleFile(e);
                    break;
                case "MergeDocuments":
                    MergeDocuments(e);
                    break;
                case "CustomSave":
                    SaveOnServer(e);
                    break;
                default:
                    // DownloadAttachment_N, where N is the 0-based attachment index.
                    if (e.actionName.StartsWith("DownloadAttachment_", StringComparison.Ordinal))
                    {
                        DownloadAttachment(e, e.actionName.Substring("DownloadAttachment_".Length));
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------------
        //  Attachments: emails keep their attachments as PDF embedded files once
        //  GdPicture has converted them; PDF documents can embed files too.
        // ---------------------------------------------------------------------
        private static GdPicturePDF LoadPdfFromDocuVieware(CustomActionEventArgs e)
        {
            MemoryStream pdfStream;
            DocumentType documentType = e.docuVieware.GetDocumentType();
            if (documentType != DocumentType.DocumentTypePDF)
            {
                // The viewer holds the original file (for example the EML bytes):
                // convert it to PDF. Emails need their format set explicitly, as it
                // is not detected from a stream.
                e.docuVieware.GetDocumentData(out MemoryStream documentStream);
                pdfStream = new MemoryStream();
                using (documentStream)
                using (var converter = new GdPictureDocumentConverter())
                {
                    documentStream.Position = 0;
                    GdPictureStatus status = documentType switch
                    {
                        DocumentType.DocumentTypeEML => converter.LoadFromStream(documentStream, GdPicture14.DocumentFormat.DocumentFormatEML),
                        DocumentType.DocumentTypeMSG => converter.LoadFromStream(documentStream, GdPicture14.DocumentFormat.DocumentFormatMSG),
                        _ => converter.LoadFromStream(documentStream)
                    };
                    if (status == GdPictureStatus.OK)
                    {
                        converter.SaveAsPDF(pdfStream, PdfConformance.PDF);
                    }
                }
                pdfStream.Position = 0;
            }
            else
            {
                e.docuVieware.GetDocumentData(out pdfStream);
            }
            var pdf = new GdPicturePDF();
            pdf.LoadFromStream(pdfStream, true);
            return pdf;
        }

        private static void GetAttachments(CustomActionEventArgs e)
        {
            if (!e.docuVieware.HasDocument())
            {
                e.result = "[]";
                return;
            }
            using var pdf = LoadPdfFromDocuVieware(e);
            var attachments = new List<string>();
            int count = pdf.GetEmbeddedFileCount();
            for (int i = 0; i < count; i++)
            {
                string name = pdf.GetEmbeddedFileName(i);
                attachments.Add(string.IsNullOrWhiteSpace(name) ? $"attachment-{i + 1}" : name);
            }
            e.result = JsonSerializer.Serialize(attachments);
        }

        private static void DownloadAttachment(CustomActionEventArgs e, string indexText)
        {
            try
            {
                int index = int.Parse(indexText);
                using var pdf = LoadPdfFromDocuVieware(e);
                if (index < 0 || index >= pdf.GetEmbeddedFileCount())
                {
                    e.result = JsonSerializer.Serialize(new { success = false, error = "No attachment at index " + index });
                    return;
                }
                byte[] fileData = Array.Empty<byte>();
                GdPictureStatus status = pdf.ExtractEmbeddedFile(index, ref fileData);
                e.result = status == GdPictureStatus.OK
                    ? JsonSerializer.Serialize(new { success = true, fileName = pdf.GetEmbeddedFileName(index), base64 = Convert.ToBase64String(fileData) })
                    : JsonSerializer.Serialize(new { success = false, error = status.ToString() });
            }
            catch (Exception ex)
            {
                e.result = JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        // ---------------------------------------------------------------------
        //  Load and merge: files picked in the browser arrive as base64.
        // ---------------------------------------------------------------------
        private static void LoadSingleFile(CustomActionEventArgs e)
        {
            try
            {
                var file = e.args == null ? null : JsonSerializer.Deserialize<UploadedFile>(e.args.ToString());
                if (file == null || string.IsNullOrWhiteSpace(file.data))
                {
                    e.message = new DocuViewareMessage("Error: no file data.");
                    return;
                }
                string fileName = string.IsNullOrWhiteSpace(file.fileName) ? "document" : Path.GetFileName(file.fileName);
                e.docuVieware.LoadFromByteArray(Convert.FromBase64String(file.data), fileName);
                e.message = new DocuViewareMessage("Document loaded");
                e.result = JsonSerializer.Serialize(new { fileName });
            }
            catch (Exception ex)
            {
                e.message = new DocuViewareMessage($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Combines the document already in the viewer (if any) with the picked files
        /// into one PDF. Every format GdPicture reads can be mixed (PDF, Office,
        /// images, emails...).
        /// </summary>
        private static void MergeDocuments(CustomActionEventArgs e)
        {
            var streams = new List<Stream>();
            try
            {
                var files = e.args == null ? null : JsonSerializer.Deserialize<List<UploadedFile>>(e.args.ToString());
                if (files == null || files.Count == 0)
                {
                    e.message = new DocuViewareMessage("Error: no document selected.");
                    return;
                }

                if (e.docuVieware.HasDocument())
                {
                    e.docuVieware.GetDocumentData(out MemoryStream loadedDocument);
                    streams.Add(EmailToPdf(loadedDocument, e.docuVieware.GetDocumentType() switch
                    {
                        DocumentType.DocumentTypeEML => ".eml",
                        DocumentType.DocumentTypeMSG => ".msg",
                        _ => ""
                    }));
                }
                foreach (var file in files)
                {
                    streams.Add(EmailToPdf(new MemoryStream(Convert.FromBase64String(file.data)), Path.GetExtension(file.fileName ?? "")));
                }

                var output = new MemoryStream();
                using var converter = new GdPictureDocumentConverter();
                GdPictureStatus status = converter.CombineToPDF(streams, output, PdfConformance.PDF);
                if (status == GdPictureStatus.OK)
                {
                    output.Position = 0;
                    e.docuVieware.LoadFromStream(output, true, MergedFileName);
                    e.message = new DocuViewareMessage("Documents merged");
                }
                else
                {
                    output.Dispose();
                    e.message = new DocuViewareMessage($"Error merging the documents: {status}");
                }
            }
            catch (Exception ex)
            {
                e.message = new DocuViewareMessage($"Error: {ex.Message}");
            }
            finally
            {
                foreach (var stream in streams)
                {
                    stream.Dispose();
                }
            }
        }

        /// <summary>
        /// CombineToPDF detects formats from the stream content, which does not work for
        /// emails: convert them to PDF first. Other streams are returned unchanged.
        /// </summary>
        private static Stream EmailToPdf(MemoryStream stream, string extension)
        {
            GdPicture14.DocumentFormat? format = extension.ToLowerInvariant() switch
            {
                ".eml" => GdPicture14.DocumentFormat.DocumentFormatEML,
                ".msg" => GdPicture14.DocumentFormat.DocumentFormatMSG,
                _ => null
            };
            if (format == null)
            {
                stream.Position = 0;
                return stream;
            }
            var pdf = new MemoryStream();
            using (stream)
            using (var converter = new GdPictureDocumentConverter())
            {
                stream.Position = 0;
                if (converter.LoadFromStream(stream, format.Value) == GdPictureStatus.OK)
                {
                    converter.SaveAsPDF(pdf, PdfConformance.PDF);
                }
            }
            pdf.Position = 0;
            return pdf;
        }

        // ---------------------------------------------------------------------
        //  Save on server: writes the current document to SavedDocuments/.
        // ---------------------------------------------------------------------
        private static void SaveOnServer(CustomActionEventArgs e)
        {
            try
            {
                if (!e.docuVieware.HasDocument())
                {
                    e.message = new DocuViewareMessage("Error: no document loaded.");
                    return;
                }

                string requestedFileName = null;
                bool isMerged = false;
                if (e.args != null && !string.IsNullOrWhiteSpace(e.args.ToString()))
                {
                    using var json = JsonDocument.Parse(e.args.ToString());
                    if (json.RootElement.TryGetProperty("fileName", out var fileNameProperty) && fileNameProperty.ValueKind == JsonValueKind.String)
                    {
                        requestedFileName = fileNameProperty.GetString();
                    }
                    if (json.RootElement.TryGetProperty("isMerged", out var isMergedProperty) && isMergedProperty.ValueKind == JsonValueKind.True)
                    {
                        isMerged = true;
                    }
                }

                string fileName = isMerged ? MergedFileName : requestedFileName;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = e.docuVieware.GetDocumentName();
                }
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = "document.pdf";
                }
                fileName = Path.GetFileName(fileName);
                // Emails and other formats are held as PDF by the viewer: save them as PDF.
                if (e.docuVieware.GetDocumentType() == DocumentType.DocumentTypePDF)
                {
                    fileName = Path.ChangeExtension(fileName, ".pdf");
                }

                Directory.CreateDirectory(SavedDocumentsDirectory);
                e.docuVieware.GetDocumentData(out MemoryStream documentStream);
                using (documentStream)
                using (var fileStream = new FileStream(Path.Combine(SavedDocumentsDirectory, fileName), FileMode.Create, FileAccess.Write))
                {
                    documentStream.Position = 0;
                    documentStream.CopyTo(fileStream);
                }

                string publicUrl = $"{AppUri}{SavedDocumentsRequestPath}/{Uri.EscapeDataString(fileName)}";
                e.result = JsonSerializer.Serialize(new { fileName, url = publicUrl });
                e.message = new DocuViewareMessage($"Saved as {fileName}");
            }
            catch (Exception ex)
            {
                e.message = new DocuViewareMessage($"Error saving the document: {ex.Message}");
            }
        }

        private sealed class UploadedFile
        {
            public string fileName { get; set; }
            public string fileType { get; set; }
            public long fileSize { get; set; }
            public string data { get; set; }
        }

        // =====================================================================
        //  ASP.NET Core pipeline
        // =====================================================================
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors(options => options.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

            // wwwroot/index.html is the client page.
            app.UseDefaultFiles();
            app.UseStaticFiles();

            Directory.CreateDirectory(SavedDocumentsDirectory);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(SavedDocumentsDirectory),
                RequestPath = SavedDocumentsRequestPath,
                ServeUnknownFileTypes = true
            });

            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }
    }
}
