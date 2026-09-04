using System.Text.RegularExpressions;
using Xunit;

namespace OverTranslate.Tests;

public class ReleaseNotesTests
{
    private static readonly string[] PublishedVersions =
        ["0.0.2", "0.0.3", "0.0.4", "0.0.5", "0.0.6", "0.0.7", "0.0.8"];

    [Fact]
    public void Every_published_version_has_a_non_empty_release_notes_section()
    {
        var path = Path.Combine(
            StringsParityTests.ProjectDirectory(), "..", "..", "docs", "ops", "RELEASE_NOTES.md");
        var notes = File.ReadAllText(Path.GetFullPath(path));

        foreach (var version in PublishedVersions)
        {
            var section = Regex.Match(
                notes,
                $"(?ms)^##\\s+{Regex.Escape(version)}\\s*$.*?(?=^##\\s+|\\z)");

            Assert.True(section.Success, $"Missing release notes section for {version}.");
            Assert.Matches(@"(?m)^[-*]\s+\S+", section.Value);
        }
    }

    [Fact]
    public void Release_workflow_reads_the_version_section_instead_of_a_fixed_template()
    {
        var path = Path.Combine(
            StringsParityTests.ProjectDirectory(), "..", "..", ".github", "workflows", "release.yml");
        var workflow = File.ReadAllText(Path.GetFullPath(path));

        Assert.Contains("docs\\ops\\RELEASE_NOTES.md", workflow);
        Assert.Contains("版本更新日志缺少章节", workflow);
        Assert.Contains("$releaseNotes", workflow);
        Assert.DoesNotContain("自动打包产出，尚未正式发布", workflow);
    }

    [Fact]
    public void Release_notes_do_not_contain_the_old_internal_publish_template()
    {
        var path = Path.Combine(
            StringsParityTests.ProjectDirectory(), "..", "..", "docs", "ops", "RELEASE_NOTES.md");
        var notes = File.ReadAllText(Path.GetFullPath(path));

        Assert.DoesNotContain("自动打包产出，尚未正式发布", notes);
        Assert.DoesNotContain("取消勾选", notes);
        Assert.DoesNotContain("Set as a pre-release", notes);
    }
}
