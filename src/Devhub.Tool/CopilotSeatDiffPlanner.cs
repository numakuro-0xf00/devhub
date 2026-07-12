namespace Devhub.Tool;

/// <summary>
/// 送信直前の seat 情報(HMAC 擬似ID化済み。doc/phase2.md Step 5「差分送信」)。
/// </summary>
/// <param name="UserId">HMAC-SHA256 済みの擬似ユーザーID(状態ファイルのキーと同じ表記)。</param>
/// <param name="LastActivityAt">seat の <c>last_activity_at</c>。</param>
/// <param name="AgentType"><see cref="CopilotSeatCandidate.AgentType"/> と同じ。</param>
public sealed record CopilotSeatSendItem(string UserId, string LastActivityAt, string? AgentType);

/// <summary>
/// <see cref="CopilotSeatDiffPlanner.Plan"/> の結果。
/// </summary>
/// <param name="ToSend">今回送信すべき seat(新規ユーザー、または前回と last_activity_at が変化したユーザー)。</param>
/// <param name="NewState">
/// 次回実行のために書き戻す状態(user_id → last_activity_at)。<paramref name="ToSend"/> に含まれない
/// (=前回と変化が無かった)ユーザーも current の値でそのまま維持される。current に存在しない
/// (=状態から消えた)ユーザーはこの辞書に含まれない(掃除される)。
/// </param>
public sealed record CopilotSeatDiffPlan(
    IReadOnlyList<CopilotSeatSendItem> ToSend,
    IReadOnlyDictionary<string, string> NewState);

/// <summary>
/// 前回の送信状態(user_id(HMAC後) → 送信済み last_activity_at)にもとづき、今回送信すべき seat を
/// 決定する純粋ロジック(doc/phase2.md Step 5「差分送信」)。定期実行での重複防止が目的:
/// 前回と同じ last_activity_at のユーザーはスキップする。
///
/// 状態ファイルの実 IO(読み書き)は呼び出し側(Program.cs)の薄い IO 層が担う。HMAC 擬似ID化
/// (<see cref="TelemetryAnonymizer"/>)も呼び出し側の責務とし、このクラスは既に擬似ID化された
/// <see cref="CopilotSeatSendItem.UserId"/> のみを扱う(salt を持ち回らない)。
///
/// 送信の実成否(TelemetrySender の結果)はこのクラスの関知しないところであり、呼び出し側が
/// 実際に送信できたかどうかに応じて <see cref="CopilotSeatDiffPlan.NewState"/> を調整してから
/// 状態ファイルに書き戻す想定(送信失敗時は前回値のまま残し、次回再送されるようにする)。
/// </summary>
public static class CopilotSeatDiffPlanner
{
    public static CopilotSeatDiffPlan Plan(
        IReadOnlyList<CopilotSeatSendItem> currentSeats,
        IReadOnlyDictionary<string, string> previousState)
    {
        ArgumentNullException.ThrowIfNull(currentSeats);
        ArgumentNullException.ThrowIfNull(previousState);

        var toSend = new List<CopilotSeatSendItem>();
        var newState = new Dictionary<string, string>();

        foreach (var seat in currentSeats)
        {
            // current に存在するユーザーはすべて newState に載せる(=状態から消えたユーザーの掃除は
            // 「current に無いものは newState に載せない」だけで自然に実現される)。
            newState[seat.UserId] = seat.LastActivityAt;

            if (previousState.TryGetValue(seat.UserId, out var previousTimestamp)
                && previousTimestamp == seat.LastActivityAt)
            {
                continue; // 前回と同じ last_activity_at のためスキップ(重複防止)。
            }

            toSend.Add(seat);
        }

        return new CopilotSeatDiffPlan(toSend, newState);
    }

    /// <summary>
    /// 送信結果(<see cref="TelemetrySender.Send"/> の bool 結果を呼び出し側が集約した
    /// <paramref name="failedUserIds"/>)にもとづき、状態ファイルへ書き戻す最終状態を確定する純粋関数
    /// (doc/phase2.md Step 5「差分送信」)。
    ///
    /// <paramref name="plan"/>.<see cref="CopilotSeatDiffPlan.NewState"/> を起点とし、
    /// <paramref name="plan"/>.<see cref="CopilotSeatDiffPlan.ToSend"/> のうち送信に失敗した
    /// (<paramref name="failedUserIds"/> に含まれる)ユーザーだけを巻き戻す:
    ///   - 既存ユーザー(<paramref name="previousState"/> に前回値がある)は前回値へ戻す(次回再送対象になる)。
    ///   - 新規ユーザー(<paramref name="previousState"/> に前回値が無い)は記録しない(未送信のまま)。
    /// 送信に成功した、または元々送信対象でなかった(=前回と変化が無かった)ユーザーの状態は変更しない。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReconcileAfterSend(
        CopilotSeatDiffPlan plan,
        IReadOnlyDictionary<string, string> previousState,
        IReadOnlyCollection<string> failedUserIds)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(previousState);
        ArgumentNullException.ThrowIfNull(failedUserIds);

        var finalState = new Dictionary<string, string>(plan.NewState);

        foreach (var item in plan.ToSend)
        {
            if (!failedUserIds.Contains(item.UserId)) continue;

            if (previousState.TryGetValue(item.UserId, out var previousTimestamp))
            {
                finalState[item.UserId] = previousTimestamp;
            }
            else
            {
                finalState.Remove(item.UserId);
            }
        }

        return finalState;
    }
}
