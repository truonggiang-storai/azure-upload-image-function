using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UploadAndResize
{
    public static class UploadAndResize
    {
        [FunctionName("UploadAndResize")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var formdata = await req.ReadFormAsync();
            IFormFile file = req.Form.Files["file"];
            string name = formdata["name"];

            if (file != null && file.Length > 0)
            {
                var extension = Path.GetExtension(file.FileName);
                var encoder = GetEncoder(extension);

                if (encoder != null)
                {
                    var blobName = string.IsNullOrEmpty(name) ? file.FileName : name;

                    string connection = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                    string containerName = Environment.GetEnvironmentVariable("ContainerName");
                    var blobClient = new BlobContainerClient(connection, containerName);

                    var blob = blobClient.GetBlobClient(blobName);

                    Stream myBlob = file.OpenReadStream();
                    await blob.UploadAsync(myBlob);

                    var PregenerateWidthsJson = Environment.GetEnvironmentVariable("PregenerateWidths");
                    var PregenerateWidths = JsonConvert.DeserializeObject<List<string>>(PregenerateWidthsJson);

                    foreach (var width in PregenerateWidths)
                    {
                        myBlob.Position = 0;

                        using (var output = new MemoryStream())
                        using (Image<Rgba32> image = Image.Load<Rgba32>(myBlob))
                        {
                            int newWidth = Convert.ToInt32(width);

                            if (newWidth < image.Width)
                            {
                                var divisor = image.Width / newWidth;
                                var newHeight = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                                image.Mutate(x => x.Resize(newWidth, newHeight));
                                image.Save(output, encoder);
                                output.Position = 0;

                                var newBlobNameWithoutExtension = Path.GetFileNameWithoutExtension(blobName);
                                var newBlobName = newBlobNameWithoutExtension + "_w" + newWidth + extension;
                                var resizeBlob = blobClient.GetBlobClient(newBlobName);
                                await resizeBlob.UploadAsync(output);
                            }
                        }
                    }

                    return new OkObjectResult("File upload successful.");
                }

                return new BadRequestObjectResult("File type not supported.");
            }

            return new BadRequestObjectResult("File is empty.");
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
    }
}