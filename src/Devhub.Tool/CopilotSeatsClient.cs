using System.Net.Http;
using System.Net.Http.Headers;

namespace Devhub.Tool;

/// <summary>
/// GitHub REST API「List all Copilot seat assignments for an organization」
/// (<c>GET /orgs/{org}/copilot/billing/seats</c>)を呼び出す薄い IO ラッパー
/// (<see cref="RulesyncRunner"/> / <see cref="SecretlintRunner"/> 相当。doc/phase2.md Step 5)。
///
/// 出典: https://docs.github.com/en/rest/copilot/copilot-user-management
///   #list-all-copilot-seat-assignments-for-an-organization
/// - 認証: <c>Authorization: Bearer &lt;token&gt;</c>(必要スコープ: <c>manage_billing:copilot</c> または
///   <c>read:org</c>。organization owner のみ閲覧可)。
/// - <c>Accept: application/vnd.github+json</c> / <c>X-GitHub-Api-Version</c> ヘッダを付与する
///   (未指定時は既定 2022-11-28 にフォールバックされるが、明示するのが公式推奨)。
/// - ページネーションは <c>page</c>(既定1) / <c>per_page</c>(既定50、最大100)。本クラスは
///   レスポンスの <c>Link</c> ヘッダ(<c>rel="next"</c>)を辿る標準的な GitHub REST API の流儀で全ページ取得する。
///
/// <c>devhub telemetry copilot-seats</c> は管理者向けコマンドであり hook の沈黙契約(常に exit 0)は適用しない。
/// そのため <c>TelemetrySender</c> と異なり、このクラスは失敗(非成功ステータス・タイムアウト・接続失敗)を
/// 例外として呼び出し側に伝播させる(呼び出し側が exit 1 として明示的に報告する)。
/// </summary>
public sealed class CopilotSeatsClient
{
    private const string DefaultBaseUrl = "https://api.github.com";

    /// <summary>
    /// GitHub REST API のバージョンヘッダに指定する値。2022-11-28 は現行の安定版であり、
    /// 未指定時のフォールバック先でもある(2028-03-10 まで長期サポート予定)。
    /// </summary>
    private const string ApiVersion = "2022-11-28";

    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;

    /// <param name="baseUrl">
    /// API ベース URL(既定 <c>https://api.github.com</c>)。GitHub Enterprise Server 等の別ベース URL、
    /// および E2E テストでのモックサーバー差し替えに使う(<c>DEVHUB_TELEMETRY_COPILOT_API_BASE_URL</c>)。
    /// </param>
    /// <param name="timeout">HTTP リクエストのタイムアウト(既定 30 秒)。</param>
    public CopilotSeatsClient(string? baseUrl = null, TimeSpan? timeout = null)
    {
        _baseUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// 組織 <paramref name="org"/> の Copilot seat 一覧を全ページ取得し、各ページのレスポンスボディ(JSON 文字列)を
    /// そのまま返す。パース(seats 抽出)は呼び出し側の <see cref="CopilotSeatEventExtractor"/> が担う。
    ///
    /// 非成功ステータスコード・タイムアウト・接続失敗はすべて例外として呼び出し側へ伝播する(exit 1 契約)。
    /// </summary>
    public IReadOnlyList<string> FetchAllSeatPagesRaw(string org, string token)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(token);

        using var client = new HttpClient { Timeout = _timeout };

        var pages = new List<string>();
        string? url = $"{_baseUrl}/orgs/{Uri.EscapeDataString(org)}/copilot/billing/seats?per_page=100&page=1";

        while (url is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("devhub-cli");
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

            using var response = client.Send(request);
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"GitHub API が失敗しました(status={(int)response.StatusCode} {response.ReasonPhrase}, url={url})");
            }

            pages.Add(body);
            url = ParseNextLink(response);
        }

        return pages;
    }

    /// <summary>
    /// <c>Link</c> レスポンスヘッダ(RFC 5988 形式。例
    /// <c>&lt;https://api.github.com/...&amp;page=2&gt;; rel="next", &lt;...&gt;; rel="last"</c>)から
    /// <c>rel="next"</c> の URL を取り出す。無ければ null(最終ページ)。
    /// </summary>
    private static string? ParseNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;

        foreach (var headerValue in values)
        {
            foreach (var part in headerValue.Split(','))
            {
                var segments = part.Split(';');
                if (segments.Length < 2) continue;

                var urlPart = segments[0].Trim();
                if (urlPart.Length < 2 || urlPart[0] != '<' || urlPart[^1] != '>') continue;

                var isNext = segments.Skip(1).Any(s => s.Trim() == "rel=\"next\"");
                if (isNext)
                {
                    return urlPart[1..^1];
                }
            }
        }

        return null;
    }
}
