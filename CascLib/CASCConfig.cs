﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CASCExplorer
{
    class CDNConfigEntry
    {
        public string Name { get; private set; }
        public string Path { get; private set; }
        public string[] Hosts { get; private set; }
    }

    class VersionsConfigEntry
    {
        public string Region { get; private set; }
        public string BuildConfig { get; private set; }
        public string CDNConfig { get; private set; }
        public int BuildId { get; private set; }
        public string VersionsName { get; private set; }
    }

    class BuildInfoEntry
    {
        public string Branch { get; private set; }
        public int Active { get; private set; }
        public string BuildKey { get; private set; }
        public string CDNKey { get; private set; }
        public string InstallKey { get; private set; }
        public int IMSize { get; private set; }
        public string CDNPath { get; private set; }
        public string[] CDNHosts { get; private set; }
        public string Tags { get; private set; }
        public string Armadillo { get; private set; }
        public string LastActivated { get; private set; }
        public string Version { get; private set; }
    }

    class VerBarConfig<T> where T : new()
    {
        private readonly List<T> Data = new List<T>();

        public int Count { get { return Data.Count; } }

        public T this[int index]
        {
            get { return Data[index]; }
        }

        public static VerBarConfig<T> ReadVerBarConfig(Stream stream)
        {
            using (var sr = new StreamReader(stream))
                return ReadVerBarConfig(sr);
        }

        public static VerBarConfig<T> ReadVerBarConfig(TextReader reader)
        {
            var result = new VerBarConfig<T>();
            string line;

            int lineNum = 0;

            string[] fields = null;

            while ((line = reader.ReadLine()) != null)
            {
                if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#")) // skip empty lines and comments
                    continue;

                string[] tokens = line.Split(new char[] { '|' });

                Type t = typeof(T);

                if (lineNum == 0) // keys
                {
                    fields = new string[tokens.Length];

                    for (int i = 0; i < tokens.Length; ++i)
                    {
                        fields[i] = tokens[i].Split(new char[] { '!' })[0].Replace(" ", "");
                    }
                }
                else // values
                {
                    var v = new T();

                    for (int i = 0; i < tokens.Length; ++i)
                    {
                        Type propt = t.GetProperty(fields[i]).PropertyType;

                        if (propt.IsArray)
                        {
                            t.GetProperty(fields[i]).SetValue(v, tokens[i].Split(' '));
                        }
                        else
                        {
                            t.GetProperty(fields[i]).SetValue(v, Convert.ChangeType(tokens[i], propt));
                        }
                    }

                    result.Data.Add(v);
                }

                lineNum++;
            }

            return result;
        }
    }

    public class KeyValueConfig
    {
        private readonly Dictionary<string, List<string>> Data = new Dictionary<string, List<string>>();

        public List<string> this[string key]
        {
            get { return Data[key]; }
        }

        public static KeyValueConfig ReadKeyValueConfig(Stream stream)
        {
            using (var sr = new StreamReader(stream))
            {
                return ReadKeyValueConfig(sr);
            }
        }

        public static KeyValueConfig ReadKeyValueConfig(TextReader reader)
        {
            var result = new KeyValueConfig();
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#")) // skip empty lines and comments
                    continue;

                string[] tokens = line.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length != 2)
                    throw new Exception("KeyValueConfig: tokens.Length != 2");

                var values = tokens[1].Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var valuesList = values.ToList();
                result.Data.Add(tokens[0].Trim(), valuesList);
            }
            return result;
        }
    }

    public class CASCConfig
    {
        //KeyValueConfig _BuildConfig;
        KeyValueConfig _CDNConfig;

        List<KeyValueConfig> _Builds;

        VerBarConfig<BuildInfoEntry> _BuildInfo;
        VerBarConfig<CDNConfigEntry> _CDNData;
        VerBarConfig<VersionsConfigEntry> _VersionsData;

        string region;
        string product;

        public static CASCConfig LoadOnlineStorageConfig(string product, string region)
        {
            var config = new CASCConfig { OnlineMode = true };

            config.region = region;
            config.product = product;

            using (var cdnsStream = CDNIndexHandler.OpenFileDirect(String.Format("http://us.patch.battle.net/{0}/cdns", product)))
            {
                config._CDNData = VerBarConfig<CDNConfigEntry>.ReadVerBarConfig(cdnsStream);
            }

            using (var versionsStream = CDNIndexHandler.OpenFileDirect(String.Format("http://us.patch.battle.net/{0}/versions", product)))
            {
                config._VersionsData = VerBarConfig<VersionsConfigEntry>.ReadVerBarConfig(versionsStream);
            }

            int index = 0;

            for (int i = 0; i < config._VersionsData.Count; ++i)
            {
                if (config._VersionsData[i].Region == region)
                {
                    index = i;
                    break;
                }
            }

            config.Build = config._VersionsData[index].BuildId;

            //string buildKey = config._VersionsData[index].BuildConfig;
            //using (Stream stream = CDNIndexHandler.OpenConfigFileDirect(config.CDNUrl, buildKey))
            //{
            //    config._BuildConfig = KeyValueConfig.ReadKeyValueConfig(stream);
            //}

            string cdnKey = config._VersionsData[index].CDNConfig;
            using (Stream stream = CDNIndexHandler.OpenConfigFileDirect(config.CDNUrl, cdnKey))
            {
                config._CDNConfig = KeyValueConfig.ReadKeyValueConfig(stream);
            }

            config.ActiveCDNBuild = 0;

            config._Builds = new List<KeyValueConfig>();

            for (int i = 0; i < config._CDNConfig["builds"].Count; i++)
            {
                try
                {
                    using (Stream stream = CDNIndexHandler.OpenConfigFileDirect(config.CDNUrl, config._CDNConfig["builds"][i]))
                    {
                        var cfg = KeyValueConfig.ReadKeyValueConfig(stream);
                        config._Builds.Add(cfg);
                    }
                }
                catch
                {

                }
            }

            return config;
        }

        public static CASCConfig LoadLocalStorageConfig(string basePath)
        {
            var config = new CASCConfig { OnlineMode = false, BasePath = basePath };

            bool isHots = File.Exists(Path.Combine(basePath, "Heroes of the Storm.exe"));

            string buildInfoPath = Path.Combine(basePath, ".build.info");

            using (Stream buildInfoStream = new FileStream(buildInfoPath, FileMode.Open))
            {
                config._BuildInfo = VerBarConfig<BuildInfoEntry>.ReadVerBarConfig(buildInfoStream);
            }

            BuildInfoEntry bi = null;

            for (int i = 0; i < config._BuildInfo.Count; ++i)
            {
                if (config._BuildInfo[i].Active == 1)
                {
                    bi = config._BuildInfo[i];
                    break;
                }
            }

            if (bi == null)
                throw new Exception("Can't find active BuildInfoEntry");

            try
            {
                config.Build = Convert.ToInt32(bi.Version.Split('.')[3]);
            }
            catch
            {
                try
                {
                    config.Build = Convert.ToInt32(System.Text.RegularExpressions.Regex.Match(bi.Version, "\\d+").Value);
                }
                catch
                {
                    config.Build = -1;
                }
            }

            string dataFolder = isHots ? "HeroesData" : "Data";

            config.ActiveCDNBuild = 0;
            string buildKey = bi.BuildKey;
            string buildCfgPath = Path.Combine(basePath, String.Format("{0}\\config\\", dataFolder), buildKey.Substring(0, 2), buildKey.Substring(2, 2), buildKey);
            using (Stream stream = new FileStream(buildCfgPath, FileMode.Open))
            {
                config._Builds.Add(KeyValueConfig.ReadKeyValueConfig(stream));
            }

            string cdnKey = bi.CDNKey;
            string cdnCfgPath = Path.Combine(basePath, String.Format("{0}\\config\\", dataFolder), cdnKey.Substring(0, 2), cdnKey.Substring(2, 2), cdnKey);
            using (Stream stream = new FileStream(cdnCfgPath, FileMode.Open))
            {
                config._CDNConfig = KeyValueConfig.ReadKeyValueConfig(stream);
            }

            return config;
        }

        public string BasePath { get; private set; }

        public bool OnlineMode { get; private set; }

        public int Build { get; private set; }

        public int ActiveCDNBuild { get; set; }

        public string BuildName { get { return _Builds[ActiveCDNBuild]["build-name"][0]; } }

        public string Product { get { return product; } }

        public byte[] RootMD5
        {
            get { return _Builds[ActiveCDNBuild]["root"][0].ToByteArray(); }
        }

        public byte[] DownloadMD5
        {
            get { return _Builds[ActiveCDNBuild]["download"][0].ToByteArray(); }
        }

        public byte[] InstallMD5
        {
            get { return _Builds[ActiveCDNBuild]["install"][0].ToByteArray(); }
        }

        public byte[] EncodingMD5
        {
            get { return _Builds[ActiveCDNBuild]["encoding"][0].ToByteArray(); }
        }

        public byte[] EncodingKey
        {
            get { return _Builds[ActiveCDNBuild]["encoding"][1].ToByteArray(); }
        }

        public string BuildUID
        {
            get { return _Builds[ActiveCDNBuild]["build-uid"][0]; }
        }

        public string CDNUrl
        {
            get
            {
                if (OnlineMode)
                {
                    int index = 0;

                    for (int i = 0; i < _CDNData.Count; ++i)
                    {
                        if (_CDNData[i].Name == region)
                        {
                            index = i;
                            break;
                        }
                    }
                    // use first CDN address for now
                    string cdns = _CDNData[index].Hosts[0];
                    return String.Format("http://{0}/{1}", cdns, _CDNData[index].Path);
                }
                else
                    return String.Format("http://{0}{1}", _BuildInfo[0].CDNHosts[0], _BuildInfo[0].CDNPath);
            }
        }

        public List<string> Archives
        {
            get { return _CDNConfig["archives"]; }
        }

        public string ArchiveGroup
        {
            get { return _CDNConfig["archive-group"][0]; }
        }

        public List<string> PatchArchives
        {
            get { return _CDNConfig["patch-archives"]; }
        }

        public string PatchArchiveGroup
        {
            get { return _CDNConfig["patch-archive-group"][0]; }
        }

        public List<KeyValueConfig> Builds
        {
            get { return _Builds; }
        }
    }
}
