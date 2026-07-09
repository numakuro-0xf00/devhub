using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class EnvFileScannerTests
{
    [Fact]
    public void rulesyncディレクトリが無ければnullを返す()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-env-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var result = EnvFileScanner.FindRequiredVariables(root);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void rulesyncディレクトリはあるがプレースホルダを含むファイルが無ければ空を返す()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-env-test-" + Guid.NewGuid());
        var rulesyncDir = Path.Combine(root, ".rulesync");
        Directory.CreateDirectory(rulesyncDir);
        try
        {
            File.WriteAllText(Path.Combine(rulesyncDir, "mcp.json"), "{ \"servers\": {} }");

            var result = EnvFileScanner.FindRequiredVariables(root);

            Assert.NotNull(result);
            Assert.Empty(result!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // これは EnvFileScanner(実ファイル走査)と EnvPlaceholderScanner(プレースホルダ抽出)の
    // 配線を検証する統合テストである。対象拡張子のフィルタリング・複数ファイルにまたがる集約・
    // 重複排除が一貫して動くことを確認する。
    [Fact]
    public void 対象拡張子のファイルを再帰的に走査しプレースホルダを重複排除して集約する()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-env-test-" + Guid.NewGuid());
        var rulesyncDir = Path.Combine(root, ".rulesync");
        var rulesDir = Path.Combine(rulesyncDir, "rules");
        Directory.CreateDirectory(rulesDir);
        try
        {
            // 対象拡張子: .json, .md, .txt, .aiignore を含む。
            File.WriteAllText(Path.Combine(rulesyncDir, "mcp.json"), "{ \"token\": \"${API_TOKEN}\" }");
            File.WriteAllText(Path.Combine(rulesDir, "overview.md"), "接続先: ${API_TOKEN} ${DB_HOST:-localhost}");
            File.WriteAllText(Path.Combine(rulesyncDir, ".aiignore"), "# ${IGNORE_VAR} を含む");
            File.WriteAllText(Path.Combine(rulesyncDir, "notes.txt"), "${TXT_VAR}");

            // 対象外拡張子: .bin は走査されないため、ここにあるプレースホルダは無視される。
            File.WriteAllText(Path.Combine(rulesyncDir, "asset.bin"), "${SHOULD_NOT_APPEAR}");

            var result = EnvFileScanner.FindRequiredVariables(root);

            Assert.NotNull(result);
            // 走査順(ファイルパスの辞書順)に依存させず、集合として一致することを確認する。
            var expected = new[] { "API_TOKEN", "DB_HOST", "IGNORE_VAR", "TXT_VAR" };
            Assert.Equal(expected.OrderBy(x => x), result!.OrderBy(x => x));
            Assert.DoesNotContain("SHOULD_NOT_APPEAR", result!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void toml拡張子とyaml拡張子とyml拡張子のファイルも走査対象になる()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-env-test-" + Guid.NewGuid());
        var rulesyncDir = Path.Combine(root, ".rulesync");
        Directory.CreateDirectory(rulesyncDir);
        try
        {
            File.WriteAllText(Path.Combine(rulesyncDir, "config.toml"), "${TOML_VAR}");
            File.WriteAllText(Path.Combine(rulesyncDir, "config.yaml"), "${YAML_VAR}");
            File.WriteAllText(Path.Combine(rulesyncDir, "config.yml"), "${YML_VAR}");

            var result = EnvFileScanner.FindRequiredVariables(root);

            Assert.NotNull(result);
            var expected = new[] { "TOML_VAR", "YAML_VAR", "YML_VAR" };
            Assert.Equal(expected.OrderBy(x => x), result!.OrderBy(x => x));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // EnvFileScanner.EnumerateTargetFiles は Directory.EnumerateFiles で列挙したフルパスを
    // StringComparer.Ordinal で昇順ソートしてから走査する(実装コメント:
    // 「ファイル走査順を決定的にするため、フルパスの順序で列挙する」)。
    // 3ファイルはすべて rulesyncDir 直下に置くため、フルパスの大小関係はファイル名の大小関係と一致し、
    // Ordinal順は "a-first.json" < "b-second.md" < "c-third.txt" になる。
    // a-first.json で VAR_A・VAR_B が初出、b-second.md では VAR_B が既出のため無視されつつ VAR_C が初出、
    // c-third.txt で VAR_D が初出となるため、期待される出現順は [VAR_A, VAR_B, VAR_C, VAR_D] になる。
    [Fact]
    public void 複数ファイルにまたがる変数の出現順はファイル走査順パスのOrdinal順で決まる()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-env-test-" + Guid.NewGuid());
        var rulesyncDir = Path.Combine(root, ".rulesync");
        Directory.CreateDirectory(rulesyncDir);
        try
        {
            File.WriteAllText(Path.Combine(rulesyncDir, "a-first.json"), "${VAR_A} ${VAR_B}");
            File.WriteAllText(Path.Combine(rulesyncDir, "b-second.md"), "${VAR_B} ${VAR_C}");
            File.WriteAllText(Path.Combine(rulesyncDir, "c-third.txt"), "${VAR_D}");

            var result = EnvFileScanner.FindRequiredVariables(root);

            Assert.Equal(new[] { "VAR_A", "VAR_B", "VAR_C", "VAR_D" }, result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
