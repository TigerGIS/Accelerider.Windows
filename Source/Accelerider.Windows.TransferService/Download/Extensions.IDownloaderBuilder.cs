﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Polly;

namespace Accelerider.Windows.TransferService
{
    public static class DownloaderBuilderExtensions
    {
        //public static IDownloader BuildFromJson(this IDownloaderBuilder @this, string json)
        //{
        //    var configureTag = json.GetJsonValue(nameof(DownloaderSerializedData.Tag));

        //    return !string.IsNullOrEmpty(configureTag)
        //        ? @this.UseConfigure(configureTag).Build(json)
        //        : null;
        //}

        //public static IDownloaderBuilder UseConfigure(this IDownloaderBuilder @this, string configureTag)
        //{
        //    switch (configureTag)
        //    {
        //        case BaiduCloudConfigureTag:
        //            return @this.UseBaiduCloudConfigure();
        //        case OneDriveConfigureTag:
        //            return @this.UseOneDriveConfigure();
        //        case OneOneFiveCloudConfigureTag:
        //            return @this.UseOneOneFiveCloudConfigure();
        //        case SixCloudConfigureTag:
        //            return @this.UseSixCloudConfigure();
        //        default:
        //            return @this.UseDefaultConfigure();
        //    }
        //}

        public static IDownloaderBuilder UseDefaultConfigure(this IDownloaderBuilder @this) => @this
            .Configure(request =>
            {
                request.Headers.SetHeaders(new WebHeaderCollection { ["User-Agent"] = "Accelerider.Windows.DownloadEngine" });
                request.Method = "GET";
                request.Timeout = 1000 * 30;
                request.ReadWriteTimeout = 1000 * 30;
                return request;
            })
            .Configure(localPath =>
                {
                    var directoryName = Path.GetDirectoryName(localPath) ?? throw new InvalidOperationException();
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(localPath) ?? throw new InvalidOperationException();
                    var extension = Path.GetExtension(localPath);
                    for (int i = 1; File.Exists(localPath); i++)
                    {
                        localPath = $"{Path.Combine(directoryName, fileNameWithoutExtension)} ({i}){extension}";
                    }

                    return localPath;
                })
            .Configure(DefaultBlockIntervalGenerator)
            .Configure((settings, context) =>
            {
                settings.MaxConcurrent = 16;

                settings.BuildPolicy = Policy
                    .Handle<WebException>(e => e.Status != WebExceptionStatus.RequestCanceled)
                    .RetryAsync(20, (e, retryCount, policyContext) =>
                    {
                        var remotePath = ((WebException)e).Response?.ResponseUri.OriginalString;
                        if (!string.IsNullOrEmpty(remotePath))
                            context.RemotePathProvider.Score(remotePath, -3);
                    });

                settings.DownloadPolicy
                    .Catch<OperationCanceledException>((e, retryCount, blockContext) => HandleCommand.Break)
                    .Catch<WebException>((e, retryCount, blockContext) => retryCount < 3 ? HandleCommand.Retry : HandleCommand.Throw)
                    .Catch<RemotePathExhaustedException>((e, retryCount, blockContext) => HandleCommand.Throw);
            });

        private static IEnumerable<(long Offset, long Length)> DefaultBlockIntervalGenerator(long totalLength)
        {
            const long defaultBlockLength = 1024 * 1024 * 20;

            var preCount = totalLength / defaultBlockLength;

            var blockLength = preCount > 100 ? (totalLength / 99) : defaultBlockLength;

            long offset = 0;
            while (offset + blockLength < totalLength)
            {
                yield return (offset, blockLength);
                offset += blockLength;
            }

            yield return (offset, totalLength - offset);
        }

        public static IDownloaderBuilder From(this IDownloaderBuilder @this, string path)
        {
            return @this.From(new ConstantRemotePathProvider(new HashSet<string>(new[] { path })));
        }

        public static IDownloaderBuilder From(this IDownloaderBuilder @this, IEnumerable<string> paths)
        {
            return @this.From(new ConstantRemotePathProvider(new HashSet<string>(paths)));
        }
    }
}
