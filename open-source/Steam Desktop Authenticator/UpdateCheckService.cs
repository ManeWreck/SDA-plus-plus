using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class UpdateCheckService
    {
        private static readonly Regex VersionRegex = new Regex(@"\d+(?:\.\d+){1,3}", RegexOptions.Compiled);

        public async Task<UpdateCheckResult> CheckAsync(string currentVersion)
        {
            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"SDA++/{currentVersion ?? "unknown"}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await client.GetStringAsync(Branding.GithubLatestReleaseApiUrl);
            JObject release = JObject.Parse(json);
            string tag = release.Value<string>("tag_name") ?? string.Empty;
            string releaseUrl = release.Value<string>("html_url") ?? Branding.GithubReleasesUrl;

            Version installed = ParseVersion(currentVersion);
            Version latest = ParseVersion(tag);
            return new UpdateCheckResult
            {
                IsUpdateAvailable = latest > installed,
                CurrentVersion = installed,
                LatestVersion = latest,
                LatestVersionText = latest.ToString(3),
                ReleaseUrl = releaseUrl
            };
        }

        private static Version ParseVersion(string value)
        {
            Match match = VersionRegex.Match(value ?? string.Empty);
            if (!match.Success || !Version.TryParse(match.Value, out Version version))
            {
                return new Version(0, 0, 0);
            }

            return new Version(
                version.Major,
                version.Minor,
                Math.Max(0, version.Build),
                Math.Max(0, version.Revision));
        }
    }

    internal sealed class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public string LatestVersionText { get; set; }
        public string ReleaseUrl { get; set; }
    }
}
