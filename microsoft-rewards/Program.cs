using AngleSharp.Html.Parser;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

async Task LoginMicrosoft(HttpClient client, string url, Dictionary<string, string> loginData)
{
    // Get login page content.
    var htmlResp = await client.GetAsync(url);
    htmlResp.EnsureSuccessStatusCode();
    var htmlContent = await htmlResp.Content.ReadAsStringAsync();

    // Get login query url parameters.
    const string urlPostPattern = @"""urlPost""\s*:\s*\""(.*?)\""";
    var urlPostMatch = Regex.Match(htmlContent, urlPostPattern);
    var postUrl = urlPostMatch.Success ? urlPostMatch.Groups[1].Value : null;
    if (string.IsNullOrEmpty(postUrl))
    {
        const string messages = "LoginMicrosoft >> Cannot get postUrl field from html content!";
        throw new InvalidOperationException(messages);
    }

    // Get login payload data.
    const string sftTagPattern = @"""sFTTag"":""<input [^>]*?value=\\""(.+?)\\""[^>]*?/>";
    var sftTagMatch = Regex.Match(htmlContent, sftTagPattern);
    var sftValue = sftTagMatch.Success ? sftTagMatch.Groups[1].Value : null;
    if (string.IsNullOrEmpty(sftValue))
    {
        const string messages = "LoginMicrosoft >> Cannot get sftInput tag from html content!";
        throw new InvalidOperationException(messages);
    }

    // Build payload then login.
    loginData["PPFT"] = sftValue;
    var payload = new FormUrlEncodedContent(loginData);
    var loginResp = await client.PostAsync(postUrl, payload);
    loginResp.EnsureSuccessStatusCode();
}

async Task<(JsonNode, string)> GetRewardsPage(HttpClient client, string url)
{
    // Get redirect html page which contains the payload data we needed.
    var redirectResp = await client.GetAsync(url);
    redirectResp.EnsureSuccessStatusCode();
    var redirectPageContent = await redirectResp.Content.ReadAsStringAsync();
    // Console.WriteLine(redirectPageContent);

    // Parse html page to get payload field.
    var htmlParser = new HtmlParser();
    var redirectPageDocument = htmlParser.ParseDocument(redirectPageContent);
    var form = redirectPageDocument.QuerySelector("#fmHF");
    if (form is null)
    {
        const string messages = "GetRewardsPage >> Cannot get form from html page!";
        throw new InvalidOperationException(messages);
    }

    var clientInfo = form.QuerySelector("input[name='client_info']")?.GetAttribute("value");
    var code = form.QuerySelector("input[name='code']")?.GetAttribute("value");
    var state = form.QuerySelector("input[name='state']")?.GetAttribute("value");
    if (clientInfo is null || code is null || state is null)
    {
        const string messages = "GetRewardsPage >> Cannot get payload filed from form!";
        throw new InvalidOperationException(messages);
    }

    // Build request payload then post again.
    var payloadData = new Dictionary<string, string>
    {
        { "client_info", clientInfo },
        { "code", code },
        { "state", state },
    };
    var payload = new FormUrlEncodedContent(payloadData);

    var redirectUrl = url + "/signin-oidc";
    var rewardsResp = await client.PostAsync(redirectUrl, payload);
    var rewardsRespContent = await rewardsResp.Content.ReadAsStringAsync();

    // Get __RequestVerificationToken field.
    var document = htmlParser.ParseDocument(rewardsRespContent);
    var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']");
    if (tokenInput is null)
    {
        const string messages = "GetRewardsPage >> Cannot get field __RequestVerificationToken!";
        throw new InvalidOperationException(messages);
    }
    var tokenVal = tokenInput.GetAttribute("value");
    if (tokenVal is null)
    {
        const string messages = "GetRewardsPage >> Cannot get field __RequestVerificationToken!";
        throw new InvalidOperationException(messages);
    }

    // Parse rewardsRespContent to get cards infos;
    var dashboardPattern = @".*?var\s+dashboard\s*=\s*(\{.*?\});";
    var dashboardMatch = Regex.Match(rewardsRespContent, dashboardPattern);
    var dashboardData = dashboardMatch.Success ? dashboardMatch.Groups[1].Value : null;
    if (dashboardData is null)
    {
        const string messages = "GetRewardsPage >> Cannot get dashboard data from rewardsRespContent!";
        throw new InvalidOperationException(messages);
    }

    // await File.WriteAllTextAsync("data.json", dashboardData);
    // await File.WriteAllTextAsync("resp.html", rewardsRespContent);

    var jsonData = JsonNode.Parse(dashboardData);
    if (jsonData is null)
    {
        const string messages = "GetRewardsPage >> Cannot parse json data!";
        throw new InvalidOperationException(messages);
    }
    return (jsonData, tokenVal);
}

async Task ClickCard(HttpClient client, string url, JsonNode jsonData, string requestVerificationToken)
{
    // Get post payload like offerId, hash etc. from jsonData. 
    var availableCards = jsonData["morePromotions"];
    if (availableCards is null)
    {
        const string messages = "ClickCard >> Failed to read cards info. Key may invalid!";
        throw new InvalidOperationException(messages);
    }

    var cardsInfo = new List<(string, string)>();
    if (availableCards is not JsonArray dataArray)
    {
        const string messages = "ClickCard >> Cannot iteral card array!";
        throw new InvalidOperationException(messages);
    }

    foreach (JsonNode? item in dataArray)
    {
        if (item is not JsonObject dataObject)
        {
            continue;
        }
        var attributes = dataObject["attributes"];
        if (attributes is not JsonObject attributesData)
        {
            continue;
        }

        var max = attributesData["max"];
        var complete = dataObject["complete"];
        var hasLock = dataObject["exclusiveLockedFeatureStatus"];
        if (max is not null && complete is not null && hasLock is not null)
        {
            var maxScore = max.GetValue<string>();
            var isComplete = complete.GetValue<bool>();
            var hasLockVal = hasLock.GetValue<string>();
            if (!hasLockVal.Equals("notsupported", StringComparison.Ordinal) ||
                maxScore.Equals("0", StringComparison.Ordinal) ||
                isComplete)
            {
                continue;
            }

            var offerId = dataObject["offerId"];
            var hash = dataObject["hash"];
            if (offerId is not null && hash is not null)
            {
                var offerIdVal = offerId.GetValue<string>();
                var hashVal = hash.GetValue<string>();
                cardsInfo.Add((offerIdVal, hashVal));
            }
        }
    }

    // Start to post to the server.
    async Task ReportActivity(string e, string t)
    {
        var now = DateTimeOffset.Now;
        var offsetSpan = now.Offset;
        var offsetMinutes = offsetSpan.TotalMinutes;
        var payloadData = new Dictionary<string, string>
        {
            {"id", e},
            {"hash", t},
            {"timeZone", ((int)offsetMinutes).ToString()},
            {"activityAmount", 1.ToString()},
            {"dbs", 0.ToString()},
            {"form", ""},
            {"type", ""},
            {"__RequestVerificationToken", requestVerificationToken}
        };
        var payload = new FormUrlEncodedContent(payloadData);
        var resp = await client.PostAsync(url, payload);
        resp.EnsureSuccessStatusCode();
    }

    foreach (var cardPkg in cardsInfo)
    {
        await ReportActivity(cardPkg.Item1, cardPkg.Item2);
    }
}

async Task<Dictionary<string, string>> ReadJsonFileAsync(string filePath)
{
    var jsonString = await File.ReadAllTextAsync(filePath);
    var json = JsonNode.Parse(jsonString);
    if (json is null)
    {
        const string messages = "ReadJsonFileAsync >> Failed to read json file!";
        throw new InvalidOperationException(messages);
    }

    var dict = new Dictionary<string, string>();
    if (json is not JsonObject dataObject)
    {
        const string messages = "ReadJsonFileAsync >> Failed to load config!";
        throw new InvalidOperationException(messages);
    }
    foreach (var kv in dataObject)
    {
        var key = kv.Key;
        var value = kv.Value?.GetValue<string>();
        if (value is not null)
        {
            dict.Add(key, value);
        }
    }

    return dict;
}

// Load config from file such as user account, request headers etc.
var headersTask = ReadJsonFileAsync("../../../request_headers.json");
var loginDataTask = ReadJsonFileAsync("../../../user_account.json");
await Task.WhenAll(headersTask, loginDataTask);

var headers = await headersTask;
var loginData = await loginDataTask;

var cookieJar = new CookieContainer();
var handler = new HttpClientHandler
{
    CookieContainer = cookieJar,
    UseCookies = true,
};

// Configure request headers
using HttpClient client = new(handler);
client.DefaultRequestHeaders.Clear();
foreach (var header in headers)
{
    if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(header.Value);
    }
    else
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
    }
}

var loginUrl = "https://login.live.com";
await LoginMicrosoft(client, loginUrl, loginData);

var rewardsUrl = "https://rewards.bing.com";
var jsonData = await GetRewardsPage(client, rewardsUrl);

var clickCardUrl = "https://rewards.bing.com/api/reportactivity?X-Requested-With=XMLHttpRequest";
await ClickCard(client, clickCardUrl, jsonData.Item1, jsonData.Item2);