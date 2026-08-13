using System.Xml;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the parsing and the usability gate for WoL's <c>UpdateInfo.xml</c> — the file
/// the whole version chain hangs off, and until now the only part of that chain with
/// no test at all.
///
/// The case that matters is <see cref="UpdateInfoService.IsUsable"/> returning false
/// for a manifest that PARSES but lists no versions. aoe3wol's HTTP endpoint has been
/// seen serving a truncated body; when the cut lands on an element boundary the XML is
/// still well-formed, and the old code accepted it as a successful fetch. Downstream
/// that is indistinguishable from "this install matches no known version", which is
/// what once had users re-downloading 4 GB into a second folder. Rejecting it is what
/// lets the alternate URL have its turn.
///
/// The parser is deliberately structure-agnostic (it walks for &lt;version&gt; and
/// &lt;download&gt; anywhere), so the shape assertions here also pin that a wrapper
/// element cannot break it.
/// </summary>
public class UpdateInfoServiceTests
{
    // Shape copied from the real file: descending versions, `alt` (not `altLink`)
    // for the mirror, and an install-RELATIVE deleteList path.
    private const string RealisticXml = """
        <updatedata>
          <updaterinfo ver="1.4" />
          <versions>
            <version ver="1.2.0e" techmd5="AAA1" strmd5="BBB1" protomd5="CCC1" minreqdownload="0" />
            <version ver="1.2.0c2" techmd5="AAA2" strmd5="BBB2" protomd5="CCC2" minreqdownload="44" />
          </versions>
          <downloads>
            <download id="44" size="1048576" crc32="0A1B2C3D"
                      link="http://aoe3wol.com/updates/44.tar.xz"
                      alt="http://mirror.example/44.tar.xz"
                      deleteList="etc\1013c_delete.lst" version="1.2.0e"
                      postUpdatePage="http://aoe3wol.com/news" />
          </downloads>
        </updatedata>
        """;

    [Fact]
    public void ParsesVersionsAndDownloads()
    {
        var info = UpdateInfoService.ParseXml(RealisticXml);

        Assert.Equal(2, info.Versions.Count);
        Assert.Single(info.Downloads);

        // Newest first — CheckCoreAsync takes Versions[0] as "latest" purely on
        // document position, so the order is load-bearing.
        Assert.Equal("1.2.0e", info.Versions[0].Ver);
        Assert.Equal(0, info.Versions[0].MinReqDownload);
        Assert.Equal(44, info.Versions[1].MinReqDownload);
    }

    [Fact]
    public void LowercasesTheHashes_SoComparisonsAreCaseStable()
    {
        var info = UpdateInfoService.ParseXml(RealisticXml);

        Assert.Equal("aaa1", info.Versions[0].TechMd5);
        Assert.Equal("bbb1", info.Versions[0].StrMd5);
        Assert.Equal("ccc1", info.Versions[0].ProtoMd5);
    }

    [Fact]
    public void ReadsTheMirrorFromAlt_NotAltLink()
    {
        // The attribute really is `alt`; reading `altLink` would silently leave every
        // patch without its mirror.
        var dl = UpdateInfoService.ParseXml(RealisticXml).Downloads[0];

        Assert.Equal("http://mirror.example/44.tar.xz", dl.AltLink);
        Assert.Equal("http://aoe3wol.com/updates/44.tar.xz", dl.Link);
    }

    [Fact]
    public void ReadsDeleteListAsAnInstallRelativePath()
    {
        var dl = UpdateInfoService.ParseXml(RealisticXml).Downloads[0];

        Assert.Equal(@"etc\1013c_delete.lst", dl.DeleteList);
        Assert.Equal(1048576, dl.Size);
        Assert.Equal("0a1b2c3d", dl.Crc32);
    }

    // ---- The usability gate ----------------------------------------------------

    [Fact]
    public void ARealManifest_IsUsable()
    {
        Assert.True(UpdateInfoService.IsUsable(UpdateInfoService.ParseXml(RealisticXml)));
    }

    [Fact]
    public void AWellFormedButEmptyManifest_IsNotUsable()
    {
        // This is the truncation case: valid XML, zero versions. It must not count as
        // a successful fetch.
        var info = UpdateInfoService.ParseXml("<updatedata />");

        Assert.Empty(info.Versions);
        Assert.False(UpdateInfoService.IsUsable(info));
    }

    [Fact]
    public void DownloadsWithoutVersions_AreStillNotUsable()
    {
        // A body cut just after <downloads> can carry patches but no versions. Without
        // versions there is nothing to identify the install against, so the patches
        // are unusable no matter how many there are.
        const string xml = """
            <updatedata>
              <downloads>
                <download id="44" size="10" crc32="ab" link="http://x/44.tar.xz" version="1.2.0e" />
              </downloads>
            </updatedata>
            """;

        var info = UpdateInfoService.ParseXml(xml);

        Assert.Single(info.Downloads);
        Assert.False(UpdateInfoService.IsUsable(info));
    }

    [Fact]
    public void NullManifest_IsNotUsable()
    {
        Assert.False(UpdateInfoService.IsUsable(null));
    }

    [Fact]
    public void TruncatedMidElement_FailsToParseAtAll()
    {
        // The other half of the truncation story: when the cut lands mid-element the
        // XML reader rejects it, which the fetch path already treated as a failure.
        Assert.ThrowsAny<XmlException>(() =>
            UpdateInfoService.ParseXml("<updatedata><versions><version ver=\"1.2"));
    }
}
