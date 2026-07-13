using Newtonsoft.Json.Linq;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZXing;
using ZXing.Common;

namespace Steam_Desktop_Authenticator
{
    internal sealed class SteamQrLoginService
    {
        private const string MobileUserAgent = "okhttp/4.9.2";
        private const string MobileCookie = "mobileClient=android; mobileClientVersion=777777 3.10.3";
        private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36";
        private const string TwoFactorManageUrl = "https://store.steampowered.com/twofactor/manage";
        private const string TwoFactorManageActionUrl = "https://store.steampowered.com/twofactor/manage_action";
        private static readonly Regex QrUrlRegex = new Regex(@"^https?://s\.team/q/(?<version>\d+)/(?<clientId>\d+)(?:\?.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OpenIdUrlRegex = new Regex(@"^(steam://|https?://steamcommunity\.com/openid/login)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DeauthorizeSessionRegex = new Regex(@"<form[^>]*id=""deauthorize_devices_form""[\s\S]*?<input[^>]*name=""sessionid""[^>]*value=""(?<sessionid>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string CaptureAndDecodeSteamQr(QrCaptureMode captureMode, int cursorScanSize)
        {
            using (Bitmap screenshot = CaptureScreenshot(captureMode, cursorScanSize))
            {
                return DecodeQrText(screenshot);
            }
        }

        public async Task<string> HandleDecodedQrAsync(SteamGuardAccount account, string qrText)
        {
            if (string.IsNullOrWhiteSpace(qrText))
            {
                throw new InvalidOperationException(Localizer.Choose("QR code was empty.", "QR-код пуст."));
            }

            if (account == null)
            {
                throw new InvalidOperationException(Localizer.Choose("Select the Steam account that should approve the login.", "Выберите Steam-аккаунт, который должен подтвердить вход."));
            }

            Match qrMatch = QrUrlRegex.Match(qrText);
            if (qrMatch.Success)
            {
                await ApproveModernQrLoginAsync(account, qrMatch);
                return Localizer.Choose("QR login approved for the selected account.", "QR-вход подтвержден для выбранного аккаунта.");
            }

            if (OpenIdUrlRegex.IsMatch(qrText))
            {
                Process.Start(new ProcessStartInfo(qrText) { UseShellExecute = true });
                return Localizer.Choose("Opened the Steam login URL with the system handler.", "Ссылка входа Steam открыта через системный обработчик.");
            }

            throw new InvalidOperationException(Localizer.Choose("The QR code did not contain a supported Steam login URL.", "QR-код не содержит поддерживаемую ссылку входа Steam."));
        }

        public async Task TerminateAllSessionsAsync(SteamGuardAccount account)
        {
            if (account == null)
            {
                throw new InvalidOperationException(Localizer.Choose("Select an account first.", "Сначала выберите аккаунт."));
            }

            if (account.Session == null || string.IsNullOrWhiteSpace(account.Session.RefreshToken))
            {
                throw new InvalidOperationException(Localizer.Choose("The selected account does not have a saved Steam web session. Use Login again first.", "У выбранного аккаунта нет сохраненной веб-сессии Steam. Сначала выполните повторный вход."));
            }

            if (account.Session.IsRefreshTokenExpired())
            {
                throw new InvalidOperationException(Localizer.Choose("The selected account session has expired. Use Login again first.", "Сессия выбранного аккаунта истекла. Сначала выполните повторный вход."));
            }

            SteamWebSession webSession = await CreateAuthenticatedSteamWebSessionAsync(account);
            string managePage = string.IsNullOrWhiteSpace(webSession.InitialPageHtml)
                ? await DownloadSteamPageAsync(webSession.Cookies, TwoFactorManageUrl)
                : webSession.InitialPageHtml;
            if (!ContainsDeauthorizeForm(managePage))
            {
                throw new InvalidOperationException(Localizer.Choose(
                    "Steam did not open an authenticated Manage Steam Guard page for this account.",
                    "Steam не открыл авторизованную страницу управления Steam Guard для этого аккаунта."));
            }

            string formSessionId = ExtractDeauthorizeSessionId(managePage) ?? webSession.SessionId;

            NameValueCollection body = new NameValueCollection();
            body.Add("action", "deauthorize");
            body.Add("sessionid", formSessionId);

            string response = await PostSteamFormAsync(
                webSession.Cookies,
                TwoFactorManageActionUrl,
                body,
                TwoFactorManageUrl);

            if (string.IsNullOrWhiteSpace(response))
            {
                string fallbackPage = await DownloadSteamPageAsync(webSession.Cookies, TwoFactorManageUrl);
                if (!ContainsDeauthorizeForm(fallbackPage))
                {
                    throw new InvalidOperationException(Localizer.Choose(
                        "Steam did not confirm the session termination request.",
                        "Steam не подтвердил запрос на завершение сессий."));
                }

                return;
            }

            if (IsSignInPage(response))
            {
                throw new InvalidOperationException(Localizer.Choose("Steam rejected the background session while deauthorizing devices.", "Steam отклонил фоновую сессию при деавторизации устройств."));
            }
        }

        private async Task ApproveModernQrLoginAsync(SteamGuardAccount account, Match qrMatch)
        {
            await EnsureAccessTokenAsync(account);

            int version = int.Parse(qrMatch.Groups["version"].Value);
            ulong clientId = ulong.Parse(qrMatch.Groups["clientId"].Value);

            await GetAuthSessionInfoAsync(clientId, account.Session.AccessToken);

            byte[] signatureData = new byte[18];
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)version), 0, signatureData, 0, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(clientId), 0, signatureData, 2, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(account.Session.SteamID), 0, signatureData, 10, 8);

            byte[] signature = ComputeHmacSha256(account.SharedSecret, signatureData);
            await UpdateAuthSessionWithMobileConfirmationAsync(account, version, clientId, signature);
        }

        private static Bitmap CaptureScreenshot(QrCaptureMode captureMode, int cursorScanSize)
        {
            switch (captureMode)
            {
                case QrCaptureMode.MonitorUnderCursor:
                    return CaptureScreenBounds(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds);
                case QrCaptureMode.AreaAroundCursor:
                    return CaptureCursorArea(cursorScanSize);
                default:
                    return CaptureScreenBounds(System.Windows.Forms.SystemInformation.VirtualScreen);
            }
        }

        private static Bitmap CaptureCursorArea(int cursorScanSize)
        {
            int areaSize = Math.Max(250, cursorScanSize);
            Point cursor = System.Windows.Forms.Cursor.Position;
            Rectangle screenBounds = System.Windows.Forms.Screen.FromPoint(cursor).Bounds;

            Rectangle captureBounds = new Rectangle(
                cursor.X - (areaSize / 2),
                cursor.Y - (areaSize / 2),
                areaSize,
                areaSize);

            captureBounds = Rectangle.Intersect(captureBounds, screenBounds);
            return CaptureScreenBounds(captureBounds);
        }

        private static Bitmap CaptureScreenBounds(Rectangle bounds)
        {
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            return bitmap;
        }

        private static byte[] ComputeHmacSha256(string sharedSecret, byte[] message)
        {
            byte[] secret = Convert.FromBase64String(sharedSecret);
            using (HMACSHA256 hmac = new HMACSHA256(secret))
            {
                return hmac.ComputeHash(message);
            }
        }

        private static async Task EnsureAccessTokenAsync(SteamGuardAccount account)
        {
            if (account.Session == null)
            {
                throw new InvalidOperationException(Localizer.Choose("The selected account does not have a saved Steam session. Use Login again first.", "У выбранного аккаунта нет сохраненной сессии Steam. Сначала выполните повторный вход."));
            }

            if (account.Session.IsRefreshTokenExpired())
            {
                throw new InvalidOperationException(Localizer.Choose("The selected account session has expired. Use Login again first.", "Сессия выбранного аккаунта истекла. Сначала выполните повторный вход."));
            }

            if (account.Session.IsAccessTokenExpired())
            {
                await account.Session.RefreshAccessToken();
            }
        }

        private static async Task<SteamWebSession> CreateAuthenticatedSteamWebSessionAsync(SteamGuardAccount account)
        {
            await EnsureAccessTokenAsync(account);

            SteamWebSession directSession = await TryCreateDirectWebSessionAsync(account);
            if (directSession != null)
            {
                return directSession;
            }

            string sessionId = GenerateSessionId();
            CookieContainer cookies = new CookieContainer();

            NameValueCollection finalizeBody = new NameValueCollection();
            finalizeBody.Add("nonce", account.Session.RefreshToken);
            finalizeBody.Add("sessionid", sessionId);
            finalizeBody.Add("redir", "https://steamcommunity.com/login/home/?goto=");

            string finalizeResponseText = await PostSteamFormAsync(
                cookies,
                "https://login.steampowered.com/jwt/finalizelogin",
                finalizeBody,
                "https://steamcommunity.com/",
                "https://steamcommunity.com/");

            JObject finalizeJson = JObject.Parse(finalizeResponseText);
            FinalizeLoginResponse finalizeResponse =
                finalizeJson.ToObject<FinalizeLoginResponse>()
                ?? finalizeJson["response"]?.ToObject<FinalizeLoginResponse>();
            if (finalizeResponse == null || finalizeResponse.TransferInfo == null || finalizeResponse.TransferInfo.Count == 0)
            {
                throw new InvalidOperationException(Localizer.Choose("Steam returned an unexpected login transfer response.", "Steam вернул неожиданный ответ переноса входа."));
            }

            foreach (TransferInfo transfer in finalizeResponse.TransferInfo)
            {
                NameValueCollection transferBody = new NameValueCollection();
                transferBody.Add("steamID", account.Session.SteamID.ToString());

                foreach (JProperty property in transfer.Params.Properties())
                {
                    transferBody.Add(property.Name, property.Value.ToString());
                }

                await PostSteamFormAsync(cookies, transfer.Url, transferBody, transfer.Url);
            }

            AddSharedSessionCookies(cookies, sessionId);
            string managePage = await DownloadSteamPageAsync(cookies, TwoFactorManageUrl);
            if (!ContainsDeauthorizeForm(managePage))
            {
                throw new InvalidOperationException(Localizer.Choose(
                    "Steam created a temporary web session, but the Manage Steam Guard page is still not authenticated.",
                    "Steam создал временную веб-сессию, но страница управления Steam Guard все еще не авторизована."));
            }

            return new SteamWebSession(cookies, sessionId, managePage);
        }

        private static async Task<SteamWebSession> TryCreateDirectWebSessionAsync(SteamGuardAccount account)
        {
            try
            {
                CookieContainer cookies = account.Session.GetCookies();
                string managePage = await DownloadSteamPageAsync(cookies, TwoFactorManageUrl);
                if (!ContainsDeauthorizeForm(managePage))
                {
                    return null;
                }

                string sessionId = ExtractSessionIdFromCookies(cookies, TwoFactorManageUrl)
                    ?? account.Session.SessionID
                    ?? ExtractDeauthorizeSessionId(managePage)
                    ?? GenerateSessionId();

                return new SteamWebSession(cookies, sessionId, managePage);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> DownloadSteamPageAsync(CookieContainer cookies, string url)
        {
            using (CookieAwareWebClient client = new CookieAwareWebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.CookieContainer = cookies;
                client.Headers[HttpRequestHeader.UserAgent] = BrowserUserAgent;
                return await client.DownloadStringTaskAsync(url);
            }
        }

        private static async Task<string> PostSteamFormAsync(CookieContainer cookies, string url, NameValueCollection body, string referer, string origin = null)
        {
            using (CookieAwareWebClient client = new CookieAwareWebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.CookieContainer = cookies;
                client.Headers[HttpRequestHeader.UserAgent] = BrowserUserAgent;
                client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                client.Headers[HttpRequestHeader.Referer] = referer;
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    client.Headers["Origin"] = origin;
                }

                byte[] responseBytes = await client.UploadValuesTaskAsync(new Uri(url), "POST", body);
                return Encoding.UTF8.GetString(responseBytes);
            }
        }

        private static void AddSharedSessionCookies(CookieContainer cookies, string sessionId)
        {
            foreach (string domain in new[]
            {
                "steamcommunity.com",
                "store.steampowered.com",
                "help.steampowered.com",
                "checkout.steampowered.com",
                "steam.tv"
            })
            {
                cookies.Add(new Cookie("sessionid", sessionId, "/", domain));
            }
        }

        private static string ExtractSessionIdFromCookies(CookieContainer cookies, string url)
        {
            try
            {
                CookieCollection cookieCollection = cookies.GetCookies(new Uri(url));
                return cookieCollection["sessionid"]?.Value;
            }
            catch
            {
                return null;
            }
        }

        private static bool ContainsDeauthorizeForm(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            return html.IndexOf("deauthorize_devices_form", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("manage_action", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSignInPage(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            return html.IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("signin", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("loginForm", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("/login/home", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractDeauthorizeSessionId(string managePageHtml)
        {
            if (string.IsNullOrWhiteSpace(managePageHtml))
            {
                return null;
            }

            Match match = DeauthorizeSessionRegex.Match(managePageHtml);
            return match.Success ? match.Groups["sessionid"].Value : null;
        }

        private static string GenerateSessionId()
        {
            byte[] bytes = new byte[12];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte current in bytes)
            {
                builder.Append(current.ToString("x2"));
            }

            return builder.ToString();
        }

        private static async Task GetAuthSessionInfoAsync(ulong clientId, string accessToken)
        {
            NameValueCollection body = new NameValueCollection();
            body.Add("client_id", clientId.ToString());

            string response = await SteamWeb.POSTRequest(
                "https://api.steampowered.com/IAuthenticationService/GetAuthSessionInfo/v1/?access_token=" + Uri.EscapeDataString(accessToken),
                null,
                body);

            JObject.Parse(response);
        }

        private static async Task UpdateAuthSessionWithMobileConfirmationAsync(SteamGuardAccount account, int version, ulong clientId, byte[] signature)
        {
            NameValueCollection body = new NameValueCollection();
            body.Add("version", version.ToString());
            body.Add("client_id", clientId.ToString());
            body.Add("steamid", account.Session.SteamID.ToString());
            body.Add("signature", Convert.ToBase64String(signature));
            body.Add("confirm", "true");
            body.Add("persistence", "1");

            string response = await SteamWeb.POSTRequest(
                "https://api.steampowered.com/IAuthenticationService/UpdateAuthSessionWithMobileConfirmation/v1/?access_token=" + Uri.EscapeDataString(account.Session.AccessToken),
                null,
                body);

            JObject.Parse(response);
        }

        private static string DecodeQrText(Bitmap screenshot)
        {
            BarcodeReaderGeneric reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                }
            };

            Rectangle rect = new Rectangle(0, 0, screenshot.Width, screenshot.Height);
            BitmapData bitmapData = screenshot.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bytesLength = Math.Abs(bitmapData.Stride) * screenshot.Height;
                byte[] pixels = new byte[bytesLength];
                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixels, 0, bytesLength);
                return DecodeResultOrThrow(reader.Decode(pixels, screenshot.Width, screenshot.Height, RGBLuminanceSource.BitmapFormat.BGRA32));
            }
            finally
            {
                screenshot.UnlockBits(bitmapData);
            }
        }

        private static string DecodeResultOrThrow(Result result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.Text))
            {
                throw new InvalidOperationException(Localizer.Choose("Steam QR code was not found on the screenshot.", "QR-код Steam не найден на скриншоте."));
            }

            return result.Text.Trim();
        }

        private sealed class FinalizeLoginResponse
        {
            public string SteamId { get; set; }

            public List<TransferInfo> TransferInfo { get; set; } = new List<TransferInfo>();
        }

        private sealed class TransferInfo
        {
            public string Url { get; set; }

            public JObject Params { get; set; }
        }

        private sealed class SteamWebSession
        {
            public SteamWebSession(CookieContainer cookies, string sessionId, string initialPageHtml = null)
            {
                Cookies = cookies;
                SessionId = sessionId;
                InitialPageHtml = initialPageHtml;
            }

            public CookieContainer Cookies { get; }

            public string SessionId { get; }

            public string InitialPageHtml { get; }
        }
    }
}
