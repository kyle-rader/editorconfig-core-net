using System.Reflection;
using EditorConfig.Core;
using FluentAssertions;
using NUnit.Framework;

namespace EditorConfig.Tests.Caching
{
	class CachingTests : EditorConfigTestBase
	{
		private EditorConfigParser noCacheParser;
		private EditorConfigParser cacheParser;

		[SetUp]
		public void Setup()
		{
			noCacheParser = new EditorConfigParser();
			cacheParser = new EditorConfigParser(useCaching: true);
		}

		[Test]
		public void Multiple_Cache_Uses_Are_Equivalant_To_Fresh_Copies()
		{
			var settingsFirstTimeNoCache = GetConfig(MethodBase.GetCurrentMethod(), "foo.cs", parser: noCacheParser);
			var settingsSecondTimeNoCache = GetConfig(MethodBase.GetCurrentMethod(), "foo.cs", parser: noCacheParser);

			var settingsFirstTimeCache = GetConfig(MethodBase.GetCurrentMethod(), "foo.cs", parser: cacheParser);
			var settingsSecondTimeCache = GetConfig(MethodBase.GetCurrentMethod(), "foo.cs", parser: cacheParser);

			settingsFirstTimeCache.Should().BeEquivalentTo(settingsFirstTimeNoCache);
            settingsSecondTimeCache.Should().BeEquivalentTo(settingsSecondTimeNoCache);
        }
	}
}
