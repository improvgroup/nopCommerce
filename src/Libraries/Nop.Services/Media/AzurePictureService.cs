namespace Nop.Services.Media
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Threading.Tasks;
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;
    using Microsoft.AspNetCore.Http;
    using Nop.Core;
    using Nop.Core.Caching;
    using Nop.Core.Configuration;
    using Nop.Core.Domain.Catalog;
    using Nop.Core.Domain.Media;
    using Nop.Core.Infrastructure;
    using Nop.Data;
    using Nop.Services.Caching;
    using Nop.Services.Catalog;
    using Nop.Services.Configuration;
    using Nop.Services.Events;
    using Nop.Services.Seo;

    /// <summary>
    /// Picture service for Windows Azure
    /// </summary>
    public partial class AzurePictureService : PictureService
    {
        private static bool _azureBlobStorageAppendContainerName;
        private static string _azureBlobStorageConnectionString;
        private static string _azureBlobStorageContainerName;
        private static string _azureBlobStorageEndPoint;
        private static BlobContainerClient _blobContainerClient;
        private static BlobServiceClient _blobServiceClient;
        private static bool _isInitialized;
        private readonly ICacheKeyService _cacheKeyService;
        private readonly object _locker = new object();
        private readonly MediaSettings _mediaSettings;
        private readonly IStaticCacheManager _staticCacheManager;

        public AzurePictureService(
            INopDataProvider dataProvider,
            IDownloadService downloadService,
            ICacheKeyService cacheKeyService,
            IEventPublisher eventPublisher,
            IHttpContextAccessor httpContextAccessor,
            INopFileProvider fileProvider,
            IProductAttributeParser productAttributeParser,
            IRepository<Picture> pictureRepository,
            IRepository<PictureBinary> pictureBinaryRepository,
            IRepository<ProductPicture> productPictureRepository,
            ISettingService settingService,
            IStaticCacheManager staticCacheManager,
            IUrlRecordService urlRecordService,
            IWebHelper webHelper,
            MediaSettings mediaSettings,
            NopConfig config)
            : base(dataProvider,
                  downloadService,
                  eventPublisher,
                  httpContextAccessor,
                  fileProvider,
                  productAttributeParser,
                  pictureRepository,
                  pictureBinaryRepository,
                  productPictureRepository,
                  settingService,
                  urlRecordService,
                  webHelper,
                  mediaSettings)
        {
            _cacheKeyService = cacheKeyService;
            _staticCacheManager = staticCacheManager;
            _mediaSettings = mediaSettings;

            OneTimeInit(config);
        }

        protected void OneTimeInit(NopConfig config)
        {
            if (_isInitialized)
                return;

            if (string.IsNullOrEmpty(config.AzureBlobStorageConnectionString))
                throw new Exception("Azure connection string for BLOB is not specified");

            if (string.IsNullOrEmpty(config.AzureBlobStorageContainerName))
                throw new Exception("Azure container name for BLOB is not specified");

            if (string.IsNullOrEmpty(config.AzureBlobStorageEndPoint))
                throw new Exception("Azure end point for BLOB is not specified");

            lock (_locker)
            {
                if (_isInitialized)
                    return;

                _azureBlobStorageAppendContainerName = config.AzureBlobStorageAppendContainerName;
                _azureBlobStorageConnectionString = config.AzureBlobStorageConnectionString;
                _azureBlobStorageContainerName = config.AzureBlobStorageContainerName.Trim().ToLower();
                _azureBlobStorageEndPoint = config.AzureBlobStorageEndPoint.Trim().ToLower().TrimEnd('/');

                CreateCloudBlobContainer();

                _isInitialized = true;
            }
        }

        /// <summary>
        /// Create cloud blob container
        /// </summary>
        protected virtual async void CreateCloudBlobContainer()
        {
            _blobServiceClient = new BlobServiceClient(_azureBlobStorageConnectionString);
            if (_blobServiceClient is null)
            {
                throw new Exception("Azure connection string for BLOB is not working");
            }

            _blobContainerClient = _blobServiceClient.GetBlobContainerClient(_azureBlobStorageContainerName);

            await _blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
        }

        /// <summary>
        /// Delete picture thumbs
        /// </summary>
        /// <param name="picture">Picture</param>
        protected override async void DeletePictureThumbs(Picture picture)
        {
            await DeletePictureThumbsAsync(picture);
        }

        /// <summary>
        /// Get picture (thumb) local path
        /// </summary>
        /// <param name="thumbFileName">Filename</param>
        /// <returns>Local picture thumb path</returns>
        protected override string GetThumbLocalPath(string thumbFileName)
        {
            var path = _azureBlobStorageAppendContainerName ? $"{_azureBlobStorageContainerName}/" : string.Empty;

            return $"{_azureBlobStorageEndPoint}/{path}{thumbFileName}";
        }

        /// <summary>
        /// Get picture (thumb) URL 
        /// </summary>
        /// <param name="thumbFileName">Filename</param>
        /// <param name="storeLocation">Store location URL; null to use determine the current store location automatically</param>
        /// <returns>Local picture thumb path</returns>
        protected override string GetThumbUrl(string thumbFileName, string storeLocation = null)
        {
            return GetThumbLocalPath(thumbFileName);
        }

        /// <summary>
        /// Get a value indicating whether some file (thumb) already exists
        /// </summary>
        /// <param name="thumbFilePath">Thumb file path</param>
        /// <param name="thumbFileName">Thumb file name</param>
        /// <returns>Result</returns>
        protected override bool GeneratedThumbExists(string thumbFilePath, string thumbFileName)
        {
            return GeneratedThumbExistsAsync(thumbFilePath, thumbFileName).Result;
        }

        /// <summary>
        /// Save a value indicating whether some file (thumb) already exists
        /// </summary>
        /// <param name="thumbFilePath">Thumb file path</param>
        /// <param name="thumbFileName">Thumb file name</param>
        /// <param name="mimeType">MIME type</param>
        /// <param name="binary">Picture binary</param>
        protected override async void SaveThumb(string thumbFilePath, string thumbFileName, string mimeType, byte[] binary)
        {
            await SaveThumbAsync(thumbFilePath, thumbFileName, mimeType, binary);
        }

        /// <summary>
        /// Initiates an asynchronous operation to delete picture thumbs
        /// </summary>
        /// <param name="picture">Picture</param>
        protected virtual async Task DeletePictureThumbsAsync(Picture picture)
        {
            //create a string containing the blob name prefix
            var prefix = $"{picture.Id:0000000}";

            var blobServiceClient = new BlobServiceClient(_azureBlobStorageConnectionString);
            if (blobServiceClient is null)
            {
                throw new Exception("Azure connection string for BLOB is not working");
            }

            var blobContainerClient = blobServiceClient.GetBlobContainerClient(_azureBlobStorageContainerName);

            var deletionQueue = new ConcurrentQueue<Task>();
            await foreach (var blobItem in blobContainerClient.GetBlobsAsync(BlobTraits.All, BlobStates.All, prefix))
            {
                deletionQueue.Enqueue(blobContainerClient.DeleteBlobIfExistsAsync(blobItem.Name));
            }

            await Task.WhenAll(deletionQueue);

            _staticCacheManager.RemoveByPrefix(NopMediaDefaults.ThumbsExistsPrefixCacheKey);
        }

        /// <summary>
        /// Initiates an asynchronous operation to get a value indicating whether some file (thumb) already exists
        /// </summary>
        /// <param name="thumbFilePath">Thumb file path</param>
        /// <param name="thumbFileName">Thumb file name</param>
        /// <returns>Result</returns>
        protected virtual async Task<bool> GeneratedThumbExistsAsync(string thumbFilePath, string thumbFileName)
        {
            try
            {
                var key = _cacheKeyService.PrepareKeyForDefaultCache(NopMediaDefaults.ThumbExistsCacheKey, thumbFileName);

                return await _staticCacheManager.GetAsync(key, async () =>
                {
                    var blockBlob = _blobContainerClient.GetBlobClient(thumbFileName);

                    return await blockBlob.ExistsAsync();
                });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Initiates an asynchronous operation to save a value indicating whether some file (thumb) already exists
        /// </summary>
        /// <param name="thumbFilePath">Thumb file path</param>
        /// <param name="thumbFileName">Thumb file name</param>
        /// <param name="mimeType">MIME type</param>
        /// <param name="binary">Picture binary</param>
        protected virtual async Task SaveThumbAsync(string thumbFilePath, string thumbFileName, string mimeType, byte[] binary)
        {
            var blockBlob = _blobContainerClient.GetBlobClient(thumbFileName);

            if (!string.IsNullOrEmpty(mimeType) || !string.IsNullOrEmpty(_mediaSettings.AzureCacheControlHeader))
            {
                var blobHttpHeaders = new BlobHttpHeaders();

                // set mime type
                if (!string.IsNullOrEmpty(mimeType))
                {
                    blobHttpHeaders.ContentType = mimeType;
                }

                // set cache control
                if (!string.IsNullOrEmpty(_mediaSettings.AzureCacheControlHeader))
                {
                    blobHttpHeaders.CacheControl = _mediaSettings.AzureCacheControlHeader;
                }

                await blockBlob.SetHttpHeadersAsync(blobHttpHeaders);
            }

            using (var ms = new MemoryStream(binary))
            {
                await blockBlob.UploadAsync(ms);
            }

            _staticCacheManager.RemoveByPrefix(NopMediaDefaults.ThumbsExistsPrefixCacheKey);
        }
    }
}