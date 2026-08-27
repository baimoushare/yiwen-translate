using OverTranslate.Services;
using Xunit;

namespace OverTranslate.Tests;

/// <summary>
/// The script guessing AUTO has to do before a Windows voice can be picked for it.
/// </summary>
public class WindowsVoiceLanguageTests
{
    [Theory]
    [InlineData("ZH",      "zh")]
    [InlineData("ZH-HANS", "zh")]
    [InlineData("ZH-HANT", "zh")]
    [InlineData("JA",      "ja")]
    [InlineData("KO",      "ko")]
    [InlineData("EN",      "en")]
    [InlineData("EN-US",   "en")]
    [InlineData("DE",      "de")]
    public void AChosenLanguage_MapsStraightToItsPrefix(string code, string expected)
    {
        Assert.Equal(expected, TtsService.ResolveWindowsLanguagePrefix(code, "any text"));
    }

    [Theory]
    [InlineData("こんにちは",        "ja")] // kana
    [InlineData("翻訳のテスト",      "ja")] // han + kana — kana wins
    [InlineData("안녕하세요",         "ko")] // hangul
    [InlineData("你好世界",           "zh")] // han only
    [InlineData("Hello, world",     "en")]
    [InlineData("12345 !@#",        "en")] // nothing to read a language from
    public void Automatic_MakesItsGuessFromTheTextItself(string text, string expected)
    {
        Assert.Equal(expected, TtsService.ResolveWindowsLanguagePrefix("AUTO", text));
    }
}
