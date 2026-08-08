using OD.Installer.Builder;

namespace OD.Installer.Builder.Tests;
public sealed class CliParserTests
{
    [Fact]
    public void Parse_AcceptsBuildWithNoOptions()
    {
        var result = CliParser.Parse(["build", "installer.json"]);
        Assert.Equal("build", result.Command);
        Assert.Equal("installer.json", result.ManifestPath);
        Assert.False(result.Force);
        Assert.False(result.Verbose);
        Assert.Null(result.Output);
    }

    [Fact]
    public void Parse_AcceptsAllOptions()
    {
        var result = CliParser.Parse(["build", "installer.json", "--output", "out", "--force", "--verbose"]);
        Assert.Equal("out", result.Output);
        Assert.True(result.Force);
        Assert.True(result.Verbose);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        var result = CliParser.Parse(["BUILD", "installer.json", "--Force", "--VERBOSE"]);
        Assert.Equal("installer.json", result.ManifestPath);
        Assert.True(result.Force);
        Assert.True(result.Verbose);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Parse_ShowsHelp(string argument)
    {
        Assert.True(CliParser.Parse([argument]).ShowHelp);
    }

    [Fact]
    public void Parse_ShowsHelpAfterCommand()
    {
        Assert.True(CliParser.Parse(["build", "installer.json", "--help"]).ShowHelp);
    }

    [Fact]
    public void Parse_ShowsVersion()
    {
        Assert.True(CliParser.Parse(["--version"]).ShowVersion);
    }

    [Fact]
    public void Parse_RejectsEmptyArguments()
    {
        Assert.Throws<ArgumentException>(() => CliParser.Parse([]));
    }

    [Fact]
    public void Parse_RejectsUnknownCommand()
    {
        Assert.Throws<ArgumentException>(() => CliParser.Parse(["install"]));
    }

    [Fact]
    public void Parse_RejectsMissingManifest()
    {
        Assert.Throws<ArgumentException>(() => CliParser.Parse(["build"]));
    }

    [Fact]
    public void Parse_RejectsUnknownOption()
    {
        Assert.Throws<ArgumentException>(() => CliParser.Parse(["build", "installer.json", "--nope"]));
    }

    [Fact]
    public void Parse_RejectsMissingOutputValue()
    {
        Assert.Throws<ArgumentException>(() => CliParser.Parse(["build", "installer.json", "--output"]));
    }

    [Fact]
    public void Parse_RejectsUnexpectedArgument()
    {
        Assert.Throws<ArgumentException>(() => CliParser.Parse(["build", "installer.json", "extra"]));
    }
}
