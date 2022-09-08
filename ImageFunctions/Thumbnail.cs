// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

// Learn how to locally debug an Event Grid-triggered function:
//    https://aka.ms/AA30pjh

// Use for local testing:
//   https://{ID}.ngrok.io/runtime/webhooks/EventGrid?functionName=Thumbnail

using Azure.Storage.Blobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ImageFunctions
{
    public static class Thumbnail
    {
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        private static string GetBlobNameFromUrl(string bloblUrl)
        {
            var uri = new Uri(bloblUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.Name;
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        [FunctionName("Thumbnail")]
        public static async Task Run(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            ILogger log)
        {
            try
            {
                if (input != null)
                {
                    var createdEvent = ((JObject)eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);

                    if (encoder != null)
                    {
                        var thumbnailWidthSmall = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH_SMALL"));
                        var thumbnailWidthMedium = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH_MEDIUM"));
                        var thumbnailWidthLarge = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH_LARGE"));
                        var thumbContainerName = Environment.GetEnvironmentVariable("THUMBNAIL_CONTAINER_NAME");
                        var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);
                        var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                        var blobName = GetBlobNameFromUrl(createdEvent.Url);
                        log.LogInformation("Thumbnail width small: " + thumbnailWidthSmall);
                        log.LogInformation("Thumbnail width medium: " + thumbnailWidthMedium);
                        log.LogInformation("Thumbnail width large: " + thumbnailWidthLarge);
                        byte[] bytes;
                        //Convert input stream to byte array
                        using(var memoryStream = new MemoryStream())
                        {
                            input.CopyTo(memoryStream);
                            bytes = memoryStream.ToArray();
                        }

                        using (var output_small = new MemoryStream())
                        //using (Image<Rgba32> image = Image.Load(input))
                        //SMALL THUMBNAIL
                        using (var imageToResize = Image.Load(bytes, out IImageFormat imageFormat))
                        {
                            var divisor = imageToResize.Width / thumbnailWidthSmall;
                            var height = Convert.ToInt32(Math.Round((decimal)(imageToResize.Height / divisor)));

                            imageToResize.Mutate(x => x.Resize(thumbnailWidthSmall, height));
                            imageToResize.Save(output_small, encoder);
                            output_small.Position = 0;
                            await blobContainerClient.UploadBlobAsync("small/" + blobName, output_small);
                        }
                        //MEDIUM THUMBNAIL
                        using (var output_med = new MemoryStream())
                        using (var imageToResize = Image.Load(bytes, out IImageFormat imageFormat))
                        {
                            var divisor = imageToResize.Width / thumbnailWidthMedium;
                            var height = Convert.ToInt32(Math.Round((decimal)(imageToResize.Height / divisor)));

                            imageToResize.Mutate(x => x.Resize(thumbnailWidthMedium, height));
                            imageToResize.Save(output_med, encoder);
                            output_med.Position = 0;
                            await blobContainerClient.UploadBlobAsync("medium/" + blobName, output_med);
                        }
                        // //LARGE THUMBNAIL
                        using (var output_large = new MemoryStream())
                        using (var imageToResize = Image.Load(bytes, out IImageFormat imageFormat))
                        {
                            var divisor = imageToResize.Width / thumbnailWidthLarge;
                            var height = Convert.ToInt32(Math.Round((decimal)(imageToResize.Height / divisor)));
                            log.LogInformation("Input Image (w x h): " + imageToResize.Width + " x " imageToResize.Height);
                            log.LogInformation("Divisor: " + divisor);
                            log.LogInformation("New Size (w x h): " thumbnailWidthLarge + " x " + height);
                            imageToResize.Mutate(x => x.Resize(thumbnailWidthLarge, height));
                            imageToResize.Save(output_large, encoder);
                            output_large.Position = 0;
                            await blobContainerClient.UploadBlobAsync("large/" + blobName, output_large);
                        }
                    }
                    else
                    {
                        log.LogInformation($"No encoder support for: {createdEvent.Url}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }
    }
}
