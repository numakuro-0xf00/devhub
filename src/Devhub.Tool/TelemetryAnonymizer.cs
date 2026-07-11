using System.Security.Cryptography;
using System.Text;

namespace Devhub.Tool;

/// <summary>
/// HMAC-SHA256 による擬似ID化(doc/phase2.md「イベントスキーマ」「送信コマンドの契約と構成」)。
/// session_id / user_id の生値をネットワークに流さないための最終防波堤(NFR-2)。
///
/// salt(鍵)の環境変数解決・user_id のハッシュ元(Environment.UserName / DEVHUB_TELEMETRY_USER_SEED)の
/// 解決はすべて呼び出し側(IO 層。Program.cs)の責務とし、このクラスは (salt, 生値) → 16進小文字ダイジェスト
/// の純粋関数のみを提供する。
/// </summary>
public static class TelemetryAnonymizer
{
    /// <summary>
    /// HMAC-SHA256(key=salt, message=rawValue) を計算し、小文字16進文字列で返す。
    /// salt が null または空文字列の場合は空バイト列を鍵として使う(D-P2-3 の可用性優先の方針。
    /// salt 未設定を検知して警告するかどうかは呼び出し側が DEVHUB_TELEMETRY_DEBUG=1 のときだけ行う)。
    /// 同一の (salt, rawValue) には常に同一のダイジェストを返す(決定的)。
    /// </summary>
    public static string Hash(string? salt, string rawValue)
    {
        ArgumentNullException.ThrowIfNull(rawValue);

        var keyBytes = Encoding.UTF8.GetBytes(salt ?? "");
        var messageBytes = Encoding.UTF8.GetBytes(rawValue);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(messageBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
