using System;
using AzureCDNImageResizer.Models;
using AzureCDNImageResizer.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Threading;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AzureCDNImageResizer.Services
{
    public class ImageResizerService : IImageResizerService
    {
        private readonly IOptions<ImageResizerOptions> _settings;
        private readonly ILogger _logger;
        private readonly IConfiguration _config;

        public ImageResizerService(
            IOptions<ImageResizerOptions> settings,
            IConfiguration configuration,
            ILogger<ImageResizerService> logger)
        {
            _settings = settings;
            _config = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Resizes an image to specified size and format
        /// </summary>
        /// <param name="url"></param>
        /// <param name="size"></param>
        /// <param name="output"></param>
        /// <param name="mode"></param>
        /// <param name="containerKey"></param>
        /// <param name="isVideo"></param>
        /// <returns></returns>
        public async Task<Stream> ResizeAsync(string url, string containerKey, string size, string output, string mode, bool isVideo = false)
        {
            return await GetResultStreamAsync(url, containerKey, !isVideo ? StringToImageSize(size) : null, output, mode, isVideo);
        }


        /// <summary>
        /// Download the image and pass it to get processed
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="imageSize"></param>
        /// <param name="output"></param>
        /// <param name="mode"></param>
        /// <param name="containerKey"></param>
        /// <param name="isVideo"></param>
        /// <returns></returns>
        private async Task<Stream> GetResultStreamAsync(string uri, string containerKey, ImageSize? imageSize, string output, string mode, bool isVideo = false)
        {
            // Create a BlobServiceClient object which will be used to create a container client
            var blobServiceClient = new BlobServiceClient(_config.GetConnectionString("AzureStorage"));

            // get the container name            
            try
            {
                // Create the container and return a container client object 
                var container = blobServiceClient.GetBlobContainerClient(containerKey);

                // Get a reference to a blob
                var blobClient = container.GetBlobClient(uri);

                var blobStream = await blobClient.OpenReadAsync(new BlobOpenReadOptions(allowModifications: false));

                if (output == "svg" || isVideo || imageSize is null)
                {
                    return blobStream;
                }

                if (imageSize.Value.Name == ImageSize.OriginalImageSize)
                {
                    return blobStream;
                }
                
                return CreateResizedStream(blobStream, imageSize.Value, output, mode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resize image");
                return null;
            }
        }

        /// <summary>
        /// Resizes/Crops image
        /// </summary>
        /// <param name="sourceStream"></param>
        /// <param name="size"></param>
        /// <param name="output"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        private Stream CreateResizedStream(Stream sourceStream, ImageSize size, string output, string mode)
        {
            var pipe = new Pipe();

            _ = Task.Run(async () =>
            {
                try
                {
                    using var image = await Image.LoadAsync(sourceStream);

                    ApplyResize(image, size, mode);

                    await using (var destinationStream = pipe.Writer.AsStream(true))
                    {
                        WriteImage(image, destinationStream, output);
                        await destinationStream.FlushAsync(CancellationToken.None);
                    }

                    await pipe.Writer.FlushAsync();
                    await pipe.Writer.CompleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process image stream.");
                    await pipe.Writer.CompleteAsync(ex);
                }
                finally
                {
                    await sourceStream.DisposeAsync();
                }
            });

            return pipe.Reader.AsStream();
        }

        /// <summary>
        /// Figure out if the user passed a standard size or custom size
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private ImageSize StringToImageSize(string value)
        {
            if (_settings.Value.PredefinedImageSizes.TryGetValue(value.ToLowerInvariant(), out var imageSize))
            {
                return imageSize;
            }
            return ImageSize.Parse(value);
        }

        private void ApplyResize(Image image, ImageSize size, string mode)
        {
            var selectedMode = (mode ?? string.Empty).ToLowerInvariant() switch
            {
                "boxpad" => ResizeMode.BoxPad,
                "pad" => ResizeMode.Pad,
                "max" => ResizeMode.Max,
                "min" => ResizeMode.Min,
                "stretch" => ResizeMode.Stretch,
                _ => ResizeMode.Crop
            };

            var resizeOptions = new ResizeOptions
            {
                Size = new Size(size.Width, size.Height),
                Mode = selectedMode
            };

            image.Mutate(x => x.Resize(resizeOptions));
        }

        private void WriteImage(Image image, Stream outputStream, string output)
        {
            switch (output)
            {
                case "jpeg":
                case "jpg":
                    image.SaveAsJpeg(outputStream);
                    break;
                case "gif":
                    image.SaveAsGif(outputStream);
                    break;
                case "avif":
                case "svg":
                case "webp":
                    image.SaveAsWebp(outputStream);
                    break;
                default: // png
                    image.SaveAsPng(outputStream);
                    break;
            }
        }
    }
}

