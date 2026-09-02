using OverTranslate.Services;
using Velopack.Sources;
using Xunit;

namespace OverTranslate.Tests;

public class UpdateSourceTests
{
    [Fact]
    public void PublicStableChannel_UsesLatestReleaseAssetsWithoutGitHubApi()
    {
        var source = Assert.IsType<SimpleWebSource>(
            UpdateService.CreateSource(repoUrl: null, token: null, seesPrerelease: false));

        Assert.Equal(
            "https://github.com/baimoushare/yiwen-translate/releases/latest/download/",
            source.BaseUri.AbsoluteUri);
    }

    [Fact]
    public void PrereleaseCheck_StillUsesGitHubReleaseApi()
    {
        Assert.IsType<GithubSource>(
            UpdateService.CreateSource(repoUrl: null, token: null, seesPrerelease: true));
    }

    [Fact]
    public void CustomRepository_StillUsesGitHubReleaseApi()
    {
        Assert.IsType<GithubSource>(UpdateService.CreateSource(
            "https://github.com/example/staging", "token", seesPrerelease: false));
    }
}
