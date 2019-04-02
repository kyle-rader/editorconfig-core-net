using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EditorConfig.Core
{
	/// <summary>
	/// The EditorConfigParser locates all relevant editorconfig files and makes sure they are merged correctly.
	/// </summary>
	public class EditorConfigParser
	{
		/// <summary>
		/// The current (and latest parser supported) version as string
		/// </summary>
		public static readonly string VersionString = "0.12.1";

		/// <summary>
		/// The current editorconfig version
		/// </summary>
		public static readonly Version Version = new Version(VersionString);

		private readonly GlobMatcherOptions _globOptions = new GlobMatcherOptions { MatchBase = true, Dot = true, AllowWindowsPaths = true };
		
		/// <summary>
		/// The configured name of the files holding editorconfig values, defaults to ".editorconfig"
		/// </summary>
		public string ConfigFileName { get; private set; }
		
		/// <summary>
		/// The editor config parser version in use, defaults to latest <see cref="EditorConfigParser.Version"/>
		/// </summary>
		public Version ParseVersion { get; private set; }

		/// <summary>
		/// Indicates whether or not configFiles will be cached when read.
		/// Useful for programs needing to get settings for large numbers of files
		/// where the editor config files are not changing.
		/// </summary>
		public bool UseCaching { get; private set; }

		/// <summary>
		/// The EditorConfigParser locates all relevant editorconfig files and makes sure they are merged correctly.
		/// </summary>
		/// <param name="configFileName">The name of the file(s) holding the editorconfiguration values</param>
		/// <param name="developmentVersion">Only used in testing, development to pass an older version to the parsing routine</param>
		/// <param name="useCaching">Cache EditorConfigFiles internally to avoid re-parsing the same files. Useful for getting the setting for large numbers of files quickly.</param>
		public EditorConfigParser(string configFileName = ".editorconfig", Version developmentVersion = null, bool useCaching = false)
		{
			ConfigFileName = configFileName ?? ".editorconfig";
			ParseVersion = developmentVersion ?? Version;
			UseCaching = useCaching;
		}

		/// <summary>
		/// Gets the FileConfiguration for each of the passed fileName by resolving their relevant editorconfig files.
		/// </summary>
		public IEnumerable<FileConfiguration> Parse(params string[] fileNames)
		{
			return fileNames
				.Select(f => f
					.Trim()
					.Trim(new[] { '\r', '\n' })
				)
				.Select(this.ParseFile)
				.ToList();
		}

		private FileConfiguration ParseFile(string fileName)
		{
			Debug.WriteLine(":: {0} :: {1}", this.ConfigFileName, fileName);

			var fullPath = Path.GetFullPath(fileName).Replace(@"\", "/");
			var configFiles = this.AllParentConfigFiles(fullPath);

			//All the .editorconfig files going from root =>.fileName
			var editorConfigFiles = this.ParseConfigFilesTillRoot(configFiles).Reverse();

			var sections =
				from configFile in editorConfigFiles
				from section in configFile.Sections
				let glob = this.FixGlob(section.Name, configFile.Directory)
				where this.IsMatch(glob, fullPath, configFile.Directory)
				select section;

			var allProperties =
				from section in sections
				from kv in section
				select FileConfiguration.Sanitize(kv.Key, kv.Value);

			var properties = new Dictionary<string, string>();
			foreach (var kv in allProperties)
				properties[kv.Key] = kv.Value;

			return new FileConfiguration(ParseVersion, fileName, properties);
		}

		private bool IsMatch(string glob, string fileName, string directory)
		{
			var matcher = GlobMatcher.Create(glob, _globOptions);
			var isMatch = matcher.IsMatch(fileName);
			Debug.WriteLine("{0} :: {1} \t\t:: {2}", isMatch ? "?" : "?", glob, fileName);
			return isMatch;
		}

		private string FixGlob(string glob, string directory)
		{
			switch (glob.IndexOf('/'))
			{
				case -1: glob = "**/" + glob; break;
				case 0: glob = glob.Substring(1); break;
			}
			
			//glob = Regex.Replace(glob, @"\*\*", "{*,**/**/**}");

			directory = directory.Replace(@"\", "/");
			if (!directory.EndsWith("/")) directory += "/";

			return directory + glob;
		}

		private IEnumerable<EditorConfigFile> ParseConfigFilesTillRoot(IEnumerable<string> configFiles)
		{
			foreach (var configFile in configFiles.Select(GetEditorConfigFile))
			{
				yield return configFile;
				if (configFile.IsRoot) yield break;
			}
		}

		private IEnumerable<string> AllParentConfigFiles(string fullPath)
		{
			return from parent in this.AllParentDirectories(fullPath)
				   let configFile = Path.Combine(parent, this.ConfigFileName)
				   where File.Exists(configFile)
				   select configFile;
		}

		private IEnumerable<string> AllParentDirectories(string fullPath)
		{
			var root = new DirectoryInfo(fullPath).Root.FullName;
			var dir = Path.GetDirectoryName(fullPath);
			do
			{
				if (dir == null) yield break;
				yield return dir;
				var dirInfo = new DirectoryInfo(dir);
				dir = dirInfo.Parent.FullName;
			} while (dir != root);
		}

		#region Caching
		/// <summary>
		/// Internal global cache.
		/// </summary>
		private Dictionary<string, EditorConfigFile> _cache = new Dictionary<string, EditorConfigFile>();

		/// <summary>
		/// Clears the global editor config file cache.
		/// </summary>
		public void ClearCache() => _cache.Clear();

		/// <summary>
		/// Creates a new EditorConfigFile, optionally returning one from the global cache.
		/// </summary>
		/// <typeparam name="TResult"></typeparam>
		/// <param name="file"></param>
		/// <param name="useCaching"></param>
		/// <returns></returns>
		private EditorConfigFile GetEditorConfigFile(string file)
		{
			if (!UseCaching)
			{
				return new EditorConfigFile(file);
			}

			if (!_cache.ContainsKey(file))
			{
				_cache.Add(file, new EditorConfigFile(file));
			}

			return _cache[file];
		}
		#endregion
	}
}