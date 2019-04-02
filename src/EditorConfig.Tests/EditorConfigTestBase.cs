﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EditorConfig.Core;
using FluentAssertions;

namespace EditorConfig.Tests
{
	public class EditorConfigTestBase
	{
		protected void HasBogusKey(FileConfiguration file, string key)
		{
			file.Properties.Should().NotBeEmpty().And.HaveCount(1).And.ContainKey(key);
			var bogusCharset = file.Properties[key];
			bogusCharset.Should().Be("bogus");
		}

		protected FileConfiguration GetConfig(MethodBase method, string fileName, string configurationFile = ".editorconfig", EditorConfigParser parser = null)
		{
			var file = this.GetFileFromMethod(method, fileName);
			parser = parser ?? new EditorConfigParser(configurationFile);
			var fileConfigs = parser.Parse(file);
			fileConfigs.Should().NotBeEmpty();
			return fileConfigs.First();
		}

		protected string GetFileFromMethod(MethodBase method, string fileName)
		{
			var type = method.DeclaringType;
			var @namespace = type.Namespace;
			var folderSep = Path.DirectorySeparatorChar.ToString();
			var folder = @namespace.Replace("EditorConfig.Tests.", "").Replace(".", folderSep);
			var file = Path.Combine(folder, fileName.Replace(@"\", folderSep));

			var cwd = Environment.CurrentDirectory;
			file = Path.Combine(cwd.Replace(OutputPath("Release"), "").Replace(OutputPath("Debug"), ""), file);
			return file;

			string OutputPath(string configuration) => $"bin{folderSep}netcoreapp2.0{folderSep}{configuration}";
		}
	}
}
