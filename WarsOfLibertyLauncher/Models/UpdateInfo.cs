using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// Models for the UpdateInfo.xml file used by the official Wars of Liberty
/// servers. Reverse-engineered from the original Java updater (v1.4).
///
/// Structure of UpdateInfo.xml:
///
///   &lt;UpdateInfo&gt;
///     &lt;updaterinfo ver="1.4" link="http://..."/&gt;
///
///     &lt;version ver="3.2" techmd5="..." strmd5="..." protomd5="..."
///              minreqdownload="5"/&gt;
///     ...more versions...
///
///     &lt;download id="6" size="104857600" crc32="abc123"
///               link="http://..." altLink="http://..."
///               deleteList="http://..." version="3.3"
///               postUpdatePage="http://..."/&gt;
///     ...more downloads...
///   &lt;/UpdateInfo&gt;
/// </summary>
public class UpdateInfo
{
    public UpdaterInfo? UpdaterInfo { get; set; }
    public List<VersionInfo> Versions { get; set; } = new();
    public List<DownloadInfo> Downloads { get; set; } = new();
}

/// <summary>Info about the latest version of the updater itself (for self-update prompts).</summary>
public class UpdaterInfo
{
    public string Ver { get; set; } = "";
    public string Link { get; set; } = "";
}

/// <summary>
/// A known mod version, identified by the MD5 hashes of three key files:
///   data\protoy.xml, data\techtreey.xml, data\stringtabley.xml
/// </summary>
public class VersionInfo
{
    public string Ver { get; set; } = "";
    public string TechMd5 { get; set; } = "";
    public string StrMd5 { get; set; } = "";
    public string ProtoMd5 { get; set; } = "";

    /// <summary>The first download id needed to upgrade FROM this version.</summary>
    public int MinReqDownload { get; set; }
}

/// <summary>A single .tar.xz patch available on the server.</summary>
public class DownloadInfo
{
    public int Id { get; set; }
    public long Size { get; set; }
    public string Crc32 { get; set; } = "";
    public string Link { get; set; } = "";
    public string AltLink { get; set; } = "";

    /// <summary>URL of a text file listing files to delete after applying this update.</summary>
    public string DeleteList { get; set; } = "";

    /// <summary>The mod version this download brings the user TO.</summary>
    public string Version { get; set; } = "";

    /// <summary>Optional URL to open in browser after this update is applied.</summary>
    public string PostUpdatePage { get; set; } = "";
}
