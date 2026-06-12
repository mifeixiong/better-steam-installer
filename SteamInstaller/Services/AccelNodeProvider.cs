using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using SteamInstaller.Models;

namespace SteamInstaller.Services;

/// <summary>
/// 加速节点提供者 —— 优先从远程 GitHub 获取实时加速 IP 映射，
/// 获取失败时回退到内置的默认国内 CDN 加速节点。
/// </summary>
public class AccelNodeProvider : IAccelNodeProvider
{
    /// <summary>内置默认加速节点列表（国内 CDN 优化 IP）。</summary>
    private static readonly List<HostEntry> DefaultNodes = new()
    {
        new() { Host = "steamcommunity.com",       Ip = "23.36.68.116" },
        new() { Host = "store.steampowered.com",    Ip = "23.46.93.13" },
        new() { Host = "cdn.steamstatic.com",       Ip = "23.36.68.110" },
        new() { Host = "steamcdn-a.akamaihd.net",   Ip = "23.36.68.111" },
    };

    /// <summary>远程加速节点 JSON 数据源地址。</summary>
    private const string RemoteUrl = "https://raw.githubusercontent.com/user/steam-hosts/refs/heads/main/hosts.json";

    /// <summary>仅匹配 IPv4 地址的正则表达式。</summary>
    private static readonly Regex ValidIpRegex = new(
        @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;

    public AccelNodeProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
    }

    /// <summary>
    /// 获取加速节点列表：先尝试远程获取，失败则使用默认内置节点。
    /// 对远程返回的条目进行 IP 格式校验，剔除非法条目防止 hosts 文件被注入恶意内容。
    /// </summary>
    public async Task<IList<HostEntry>> FetchAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<RemoteHostsData>(RemoteUrl);
            if (response?.Entries != null && response.Entries.Count > 0)
            {
                var validEntries = ValidateAndFilter(response.Entries);
                if (validEntries.Count > 0)
                {
                    Log.Information("Fetched {Count} valid acceleration nodes from remote (filtered from {Total})",
                        validEntries.Count, response.Entries.Count);
                    return validEntries;
                }
                Log.Warning("All remote acceleration nodes failed validation, using defaults");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch remote acceleration nodes, using defaults");
        }

        Log.Information("Using {Count} default acceleration nodes", DefaultNodes.Count);
        return DefaultNodes;
    }

    /// <summary>
    /// 校验并过滤远程获取的 HostEntry 列表。
    /// 剔除 IP 地址非法（非 IPv4 格式）、域名包含危险字符的条目。
    /// </summary>
    private static List<HostEntry> ValidateAndFilter(List<HostEntry> entries)
    {
        var valid = new List<HostEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Ip) || string.IsNullOrEmpty(entry.Host))
                continue;

            if (!ValidIpRegex.IsMatch(entry.Ip))
            {
                Log.Warning("Rejected host entry with invalid IP: {Ip} -> {Host}", entry.Ip, entry.Host);
                continue;
            }

            if (!IPAddress.TryParse(entry.Ip, out _))
            {
                Log.Warning("Rejected host entry with unparseable IP: {Ip}", entry.Ip);
                continue;
            }

            if (Uri.CheckHostName(entry.Host) == UriHostNameType.Unknown)
            {
                Log.Warning("Rejected host entry with invalid hostname: {Host}", entry.Host);
                continue;
            }

            valid.Add(entry);
        }
        return valid;
    }

    /// <summary>远程 JSON 反序列化的数据结构。</summary>
    private class RemoteHostsData
    {
        [System.Text.Json.Serialization.JsonPropertyName("entries")]
        public List<HostEntry>? Entries { get; set; }
    }
}
