using OverTranslate.Services;
using Xunit;

namespace OverTranslate.Tests;

public class CustomServiceTemplateTests
{
    [Fact]
    public void GeneralAdd_OnlyOffersBlankTemplate()
    {
        var options = CustomServiceTemplate.OptionsFor(null).ToList();

        var template = Assert.Single(options);
        Assert.Equal(CustomServicePlan.Blank, template.Plan);
    }

    [Theory]
    [InlineData("glm")]
    [InlineData("kimi")]
    [InlineData("siliconflow")]
    [InlineData("qwen")]
    public void VendorWithCodingPlan_OffersOnlyOwnStandardAndCodingPlans(string vendor)
    {
        var entry = CustomServiceTemplate.VendorCards.Single(t => t.Vendor == vendor);
        var options = CustomServiceTemplate.OptionsFor(entry).ToList();

        Assert.Equal(2, options.Count);
        Assert.All(options, option => Assert.Equal(vendor, option.Vendor));
        Assert.Contains(options, option => option.Plan == CustomServicePlan.Standard);
        Assert.Contains(options, option => option.Plan == CustomServicePlan.Coding);
    }

    [Fact]
    public void VendorCards_ContainOnlyOneCardPerVendor()
    {
        var cards = CustomServiceTemplate.VendorCards.ToList();

        Assert.All(cards, card => Assert.Equal(CustomServicePlan.Standard, card.Plan));
        Assert.Equal(cards.Count, cards.Select(card => card.Vendor).Distinct().Count());
    }

    /// <summary>
    /// Vendors with a dedicated Coding Plan endpoint must carry it in the template — the official
    /// docs give them addresses distinct from the standard API (SiliconFlow shares one endpoint).
    /// </summary>
    [Theory]
    [InlineData("glm",      "https://open.bigmodel.cn/api/paas/v4",               "https://open.bigmodel.cn/api/coding/paas/v4")]
    [InlineData("kimi",     "https://api.moonshot.cn/v1",                          "https://api.kimi.com/coding/v1")]
    [InlineData("qwen",     "https://dashscope.aliyuncs.com/compatible-mode/v1",   "https://coding.dashscope.aliyuncs.com/v1")]
    public void CodingPlans_WhereOfficiallyDistinct_CarryTheirOwnEndpoint(
        string vendor, string standard, string coding)
    {
        var plans = CustomServiceTemplate.Presets.Where(t => t.Vendor == vendor).ToList();
        Assert.Equal(standard, plans.Single(t => t.Plan == CustomServicePlan.Standard).BaseUrl);
        Assert.Equal(coding, plans.Single(t => t.Plan == CustomServicePlan.Coding).BaseUrl);
    }

    [Theory]
    [InlineData("glm",  "https://open.bigmodel.cn/api/coding/paas/v4/")]
    [InlineData("kimi", "https://api.kimi.com/coding/v1")]
    [InlineData("qwen", "https://coding.dashscope.aliyuncs.com/v1")]
    [InlineData("kimi", "https://api.moonshot.cn/v1")]
    public void ACodingPlanServiceEndpoint_IsStillClaimedByItsVendorCard(string vendor, string endpoint)
    {
        Assert.True(CustomServiceTemplate.BelongsToVendor(endpoint, vendor));
        Assert.False(CustomServiceTemplate.BelongsToVendor(endpoint, "deepseek"));
    }
}
