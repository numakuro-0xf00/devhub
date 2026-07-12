using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Devhub.Tool;

/// <summary>
/// devhub telemetry send の HTTP POST を担う薄い IO ラッパー(RulesyncRunner / SecretlintRunner 相当。
/// doc/phase2.md「送信コマンドの契約と構成」)。
///
/// タイムアウト・接続失敗・DNS 解決失敗など、あらゆる例外をここで握りつぶし成否を bool で返すだけにする
/// (失敗・タイムアウトは黙って破棄し、リトライしない。hook 本体の動作を絶対にブロックしないための契約)。
/// </summary>
public sealed class TelemetrySender
{
    private readonly TimeSpan _timeout;

    /// <param name="timeout">HTTP POST のタイムアウト(既定 1000ms。DEVHUB_TELEMETRY_TIMEOUT_MS で上書き)。</param>
    public TelemetrySender(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    /// <summary>
    /// JSON ボディを endpoint へ POST する。bearerToken が非空なら Authorization: Bearer ヘッダを付ける。
    /// 成否に関わらず例外は外へ伝播させない(戻り値 false = 送信できなかった。呼び出し元は無視して exit 0 を維持する)。
    /// </summary>
    public bool Send(string endpoint, string jsonBody, string? bearerToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = _timeout };
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(bearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            using var response = client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            // タイムアウト・接続失敗・不正な endpoint 等はすべてここで飲み込む(送信コマンドの契約:常に exit 0)。
            return false;
        }
    }
}
