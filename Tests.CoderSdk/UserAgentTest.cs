using System.Reflection;
using System.Text.RegularExpressions;
using Coder.Desktop.CoderSdk;

namespace Coder.Desktop.Tests.CoderSdk;

[TestFixture]
public class UserAgentTest
{
    // The grammar every Coder client is expected to produce. Asserting against the pattern rather
    // than recomputing the platform names keeps the test honest about the mapping in UserAgent.
    private static readonly Regex Grammar = new(
        @"^(?<token>coder-desktop|coder-desktop-core)/(?<version>[0-9]+\.[0-9]+\.[0-9]+) \((windows|darwin|linux)/(386|amd64|arm|arm64)\)$",
        RegexOptions.Compiled);

    [Test(Description = "Desktop User-Agent matches the shared grammar")]
    public void DesktopMatchesGrammar()
    {
        var match = Grammar.Match(UserAgent.Build(CoderComponent.Desktop));
        Assert.That(match.Success, Is.True);
        Assert.That(match.Groups["token"].Value, Is.EqualTo("coder-desktop"));
    }

    [Test(Description = "Core User-Agent matches the shared grammar")]
    public void CoreMatchesGrammar()
    {
        var match = Grammar.Match(UserAgent.Build(CoderComponent.Core));
        Assert.That(match.Success, Is.True);
        Assert.That(match.Groups["token"].Value, Is.EqualTo("coder-desktop-core"));
    }

    [Test(Description = "Four-part assembly version is trimmed to three parts")]
    public void TrimsVersionToThreeParts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        Assert.That(version, Is.Not.Null);
        // Precondition: assembly versions are four-part, which is what makes trimming observable.
        Assert.That(version!.ToString().Split('.'), Has.Length.EqualTo(4));

        var userAgent = UserAgent.Build(CoderComponent.Desktop, assembly);

        var match = Grammar.Match(userAgent);
        Assert.That(match.Success, Is.True);
        Assert.That(match.Groups["version"].Value,
            Is.EqualTo($"{version.Major}.{version.Minor}.{version.Build}"));
        Assert.That(userAgent, Does.Not.Contain(version.ToString()));
    }

    [Test(Description = "A null assembly reports an unknown version")]
    public void NullAssemblyReportsUnknownVersion()
    {
        var userAgent = UserAgent.Build(CoderComponent.Core, null);
        Assert.That(Grammar.IsMatch(userAgent), Is.True);
        Assert.That(userAgent, Does.StartWith("coder-desktop-core/0.0.0 "));
    }

    [Test(Description = "An unknown component is rejected")]
    public void UnknownComponentThrows()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            UserAgent.Build((CoderComponent)(-1), null));
        Assert.That(ex!.ParamName, Is.EqualTo("component"));
    }
}
