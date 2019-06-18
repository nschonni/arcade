// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Build.Framework;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MSBuild = Microsoft.Build.Utilities;

namespace Microsoft.DotNet.Build.Tasks.Feed
{
    /// <summary>
    /// The intended use of this task is to push artifacts described in
    /// a build manifest to a static package feed.
    /// </summary>
    public class PublishArtifactsInManifest : MSBuild.Task
    {
        /// <summary>
        /// Configuration telling which target feed to use for each artifact category.
        /// ItemSpec: ArtifactCategory
        /// Metadata TargetURL: target URL where assets of this category should be published to.
        /// Metadata Type: type of the target feed.
        /// Metadata Token: token to be used for publishing to target feed.
        /// </summary>
        [Required]
        public ITaskItem[] TargetFeedConfig { get; set; }

        /// <summary>
        /// Full path to the assets to publish manifest.
        /// </summary>
        [Required]
        public string AssetManifestPath { get; set; }

        /// <summary>
        /// Full path to the folder containing blob assets.
        /// </summary>
        [Required]
        public string BlobAssetsBasePath { get; set; }

        /// <summary>
        /// Full path to the folder containing package assets.
        /// </summary>
        [Required]
        public string PackageAssetsBasePath { get; set; }

        /// <summary>
        /// ID of the build (in BAR/Maestro) that produced the artifacts being published.
        /// This might change in the future as we'll probably fetch this ID from the manifest itself.
        /// </summary>
        [Required]
        public int BARBuildId { get; set; }

        /// <summary>
        /// Access point to the Maestro API to be used for accessing BAR.
        /// </summary>
        [Required]
        public string MaestroApiEndpoint { get; set; }

        /// <summary>
        /// Authentication token to be used when interacting with Maestro API.
        /// </summary>
        [Required]
        public string BuildAssetRegistryToken { get; set; }

        private readonly Dictionary<string, FeedConfig> FeedConfigs = new Dictionary<string, FeedConfig>();

        private readonly Dictionary<string, List<PackageArtifactModel>> PackagesByCategory = new Dictionary<string, List<PackageArtifactModel>>();

        private readonly Dictionary<string, List<BlobArtifactModel>> BlobsByCategory = new Dictionary<string, List<BlobArtifactModel>>();


        public override bool Execute()
        {
            return ExecuteAsync().GetAwaiter().GetResult();
        }

        public async Task<bool> ExecuteAsync()
        {
            try
            {
                Log.LogMessage(MessageImportance.High, "Performing push feeds.");

                if (string.IsNullOrWhiteSpace(AssetManifestPath) || !File.Exists(AssetManifestPath))
                {
                    Log.LogError($"Problem reading asset manifest path from {AssetManifestPath}");
                }

                if (!Directory.Exists(BlobAssetsBasePath))
                {
                    Log.LogError($"Problem reading blob assets from {BlobAssetsBasePath}");
                }

                if (!Directory.Exists(PackageAssetsBasePath))
                {
                    Log.LogError($"Problem reading package assets from {PackageAssetsBasePath}");
                }

                var buildModel = BuildManifestUtil.ManifestFileToModel(AssetManifestPath, Log);

                // Parsing the manifest may fail for several reasons
                if (Log.HasLoggedErrors)
                {
                    return false;
                }

                foreach (var fc in TargetFeedConfig)
                {
                    var feedConfig = new FeedConfig()
                    {
                        TargetFeedURL = fc.GetMetadata("TargetURL"),
                        Type = fc.GetMetadata("Type"),
                        FeedKey = fc.GetMetadata("Token")
                    };

                    if (string.IsNullOrEmpty(feedConfig.TargetFeedURL) || 
                        string.IsNullOrEmpty(feedConfig.Type) || 
                        string.IsNullOrEmpty(feedConfig.FeedKey))
                    {
                        Log.LogError($"Invalid FeedConfig entry. TargetURL='{feedConfig.TargetFeedURL}' Type='{feedConfig.Type}' Token='{feedConfig.FeedKey}'");
                    }

                    FeedConfigs.Add(fc.ItemSpec.Trim().ToUpper(), feedConfig);
                }

                // Return errors from parsing FeedConfig
                if (Log.HasLoggedErrors)
                {
                    return false;
                }

                foreach (var packageAsset in buildModel.Artifacts.Packages)
                {
                    var categories = packageAsset.Attributes["Category"] ?? InferCategory(packageAsset.Id);

                    foreach (var category in categories.Split(';'))
                    {
                        if (PackagesByCategory.ContainsKey(category))
                        {
                            PackagesByCategory[category].Add(packageAsset);
                        }
                        else
                        {
                            PackagesByCategory[category] = new List<PackageArtifactModel>() { packageAsset };
                        }
                    }
                }

                foreach (var blobAsset in buildModel.Artifacts.Blobs)
                {
                    var categories = blobAsset.Attributes["Category"] ?? InferCategory(blobAsset.Id);

                    foreach (var category in categories.Split(';'))
                    {
                        if (BlobsByCategory.ContainsKey(category))
                        {
                            BlobsByCategory[category].Add(blobAsset);
                        }
                        else
                        {
                            BlobsByCategory[category] = new List<BlobArtifactModel>() { blobAsset };
                        }
                    }
                }



            }
            catch (Exception e)
            {
                Log.LogErrorFromException(e, true);
            }

            return !Log.HasLoggedErrors;
        }

        // NetCore;OSX;Deb;Rpm;Node;BinaryLayout;Installer;Checksum;Maven;VSIX
        private string InferCategory(string assetId)
        {
            assetId = assetId.Trim().ToUpper();

            if (assetId.EndsWith(".NUPKG"))
            {
                return "NetCore";
            }
            else if (assetId.EndsWith(".PKG"))
            {
                return "OSX";
            }
            else if (assetId.EndsWith(".DEB"))
            {
                return "DEB";
            }
            else if (assetId.EndsWith(".RPM"))
            {
                return "RPM";
            }
            else if (assetId.EndsWith(".NPM"))
            {
                return "NODE";
            }
            else if (assetId.EndsWith(".ZIP"))
            {
                return "BINARYLAYOUT";
            }
            else if (assetId.EndsWith(".MSI"))
            {
                return "INSTALLER";
            }
            else if (assetId.EndsWith(".SHA"))
            {
                return "CHECKSUM";
            }
            else if (assetId.EndsWith(".POM"))
            {
                return "MAVEN";
            }
            else if (assetId.EndsWith(".VSIX"))
            {
                return "VSIX";
            }
            else
            {
                return "NetCore";
            }
        }
    }

    /// <summary>
    /// Hold properties of a target feed endpoint.
    /// </summary>
    internal class FeedConfig
    {
        public string TargetFeedURL { get; set; }
        public string Type { get; set; }
        public string FeedKey { get; set; }
    }
}
