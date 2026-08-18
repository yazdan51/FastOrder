using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastOrder
{
    public class AuthenticationService
    {
        public const string AuthorizationEndpoint =
            "https://login.emofid.com/connect/authorize";

        public const string TokenEndpoint =
            "https://login.emofid.com/connect/token";

        public const string ClientId =
            "easy_pkce";

        public const string RedirectUri =
            "https://d.easytrader.ir/auth-callback";

        public const string Scope =
            "easy2_api mts_api openid profile login_delegation-api";


        public string CodeVerifier { get; private set; } = "";

        public string State { get; private set; } = "";


        public string CreateAuthorizationUrl()
        {
            CodeVerifier = CreateCodeVerifier();

            State = CreateRandomString(32);

            string codeChallenge =
                CreateCodeChallenge(CodeVerifier);


            string url =
                AuthorizationEndpoint +
                "?client_id=" +
                Uri.EscapeDataString(ClientId) +

                "&redirect_uri=" +
                Uri.EscapeDataString(RedirectUri) +

                "&response_type=code" +

                "&scope=" +
                Uri.EscapeDataString(Scope) +

                "&state=" +
                Uri.EscapeDataString(State) +

                "&code_challenge=" +
                Uri.EscapeDataString(codeChallenge) +

                "&code_challenge_method=S256";


            return url;
        }


        public async Task<TokenResponse> ExchangeCodeAsync(
            string authorizationCode)
        {
            using HttpClient client = new HttpClient();


            var parameters =
                new Dictionary<string, string>
                {
                    ["grant_type"] =
                        "authorization_code",

                    ["redirect_uri"] =
                        RedirectUri,

                    ["code"] =
                        authorizationCode,

                    ["code_verifier"] =
                        CodeVerifier,

                    ["client_id"] =
                        ClientId
                };


            using FormUrlEncodedContent content =
                new FormUrlEncodedContent(parameters);


            HttpResponseMessage response =
                await client.PostAsync(
                    TokenEndpoint,
                    content);


            string responseText =
                await response.Content
                    .ReadAsStringAsync();


            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    "Token request failed.\r\n" +
                    $"HTTP {(int)response.StatusCode}\r\n\r\n" +
                    responseText);
            }


            TokenResponse? token =
                JsonSerializer.Deserialize<TokenResponse>(
                    responseText);


            if (token == null ||
                string.IsNullOrWhiteSpace(
                    token.AccessToken))
            {
                throw new Exception(
                    "Access Token دریافت نشد.");
            }


            return token;
        }


        private static string CreateCodeVerifier()
        {
            byte[] bytes =
                RandomNumberGenerator.GetBytes(32);

            return Base64UrlEncode(bytes);
        }


        private static string CreateCodeChallenge(
            string verifier)
        {
            using SHA256 sha256 =
                SHA256.Create();


            byte[] hash =
                sha256.ComputeHash(
                    Encoding.ASCII.GetBytes(
                        verifier));


            return Base64UrlEncode(hash);
        }


        private static string CreateRandomString(
            int length)
        {
            byte[] bytes =
                RandomNumberGenerator.GetBytes(length);

            return Base64UrlEncode(bytes);
        }


        private static string Base64UrlEncode(
            byte[] data)
        {
            return Convert
                .ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}