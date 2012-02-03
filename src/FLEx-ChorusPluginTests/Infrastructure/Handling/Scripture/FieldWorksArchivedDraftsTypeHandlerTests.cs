using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FLEx_ChorusPlugin.Infrastructure;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Progress.LogBox;

namespace FLEx_ChorusPluginTests.Infrastructure.Handling.Scripture
{
	[TestFixture]
	public class FieldWorksArchivedDraftsTypeHandlerTests : BaseFieldWorksTypeHandlerTests
	{
		private TempFile _ourFile;
		private TempFile _theirFile;
		private TempFile _commonFile;

		[SetUp]
		public void TestSetup()
		{
			FieldWorksTestServices.SetupTempFilesWithExtension("." + SharedConstants.ArchivedDraft, out _ourFile, out _commonFile, out _theirFile);
		}

		[TearDown]
		public void TestTearDown()
		{
			FieldWorksTestServices.RemoveTempFiles(ref _ourFile, ref _commonFile, ref _theirFile);
		}

		[Test]
		public void DescribeInitialContentsShouldHaveAddedForLabel()
		{
			var initialContents = FileHandler.DescribeInitialContents(null, null).ToList();
			Assert.AreEqual(1, initialContents.Count());
			var onlyOne = initialContents.First();
			Assert.AreEqual("Added", onlyOne.ActionLabel);
		}

		[Test]
		public void ExtensionOfKnownFileTypesShouldBeArchivedDraft()
		{
			var extensions = FileHandler.GetExtensionsOfKnownTextFileTypes().ToArray();
			Assert.AreEqual(FieldWorksTestServices.ExpectedExtensionCount, extensions.Count(), "Wrong number of extensions.");
			Assert.IsTrue(extensions.Contains(SharedConstants.ArchivedDraft));
		}

		[Test]
		public void ShouldNotBeAbleToValidateIncorrectFormatFile()
		{
			using (var tempModelVersionFile = new TempFile("<classdata />"))
			{
				var newpath = Path.ChangeExtension(tempModelVersionFile.Path, SharedConstants.Reversal);
				File.Copy(tempModelVersionFile.Path, newpath, true);
				Assert.IsFalse(FileHandler.CanValidateFile(newpath));
				File.Delete(newpath);
			}
		}

		[Test]
		public void ShouldBeAbleToValidateInProperlyFormattedFile()
		{
			const string data =
@"<?xml version='1.0' encoding='utf-8'?>
<ArchivedDrafts>
<ScrDraft guid='0a0be0c1-39c4-44d4-842e-231680c7cd56' />
</ArchivedDrafts>";
			File.WriteAllText(_ourFile.Path, data);
			Assert.IsTrue(FileHandler.CanValidateFile(_ourFile.Path));
		}

		[Test]
		public void ShouldBeAbleToDoAllCanOperations()
		{
			const string data =
@"<?xml version='1.0' encoding='utf-8'?>
<ArchivedDrafts>
<ScrDraft guid='0a0be0c1-39c4-44d4-842e-231680c7cd56' />
</ArchivedDrafts>";
			File.WriteAllText(_ourFile.Path, data);
			Assert.IsTrue(FileHandler.CanValidateFile(_ourFile.Path));
			Assert.IsTrue(FileHandler.CanDiffFile(_ourFile.Path));
			Assert.IsTrue(FileHandler.CanMergeFile(_ourFile.Path));
			Assert.IsTrue(FileHandler.CanPresentFile(_ourFile.Path));
		}

		[Test]
		public void ShouldNotBeAbleToValidateFile()
		{
			const string data = "<?xml version='1.0' encoding='utf-8'?><classdata />";
			File.WriteAllText(_ourFile.Path, data);
			Assert.IsNotNull(FileHandler.ValidateFile(_ourFile.Path, new NullProgress()));
		}

		[Test]
		public void ShouldBeAbleToValidateFile()
		{
			const string data =
@"<?xml version='1.0' encoding='utf-8'?>
<ArchivedDrafts>
<ScrDraft guid='0a0be0c1-39c4-44d4-842e-231680c7cd56' />
</ArchivedDrafts>";
			File.WriteAllText(_ourFile.Path, data);
			Assert.IsNull(FileHandler.ValidateFile(_ourFile.Path, new NullProgress()));
		}

		[Test]
		public void BothEditedACatInConflictingWayButBothIgnoredSinceScrDraftIsImmutable()
		{
			const string commonAncestor =
				@"<?xml version='1.0' encoding='utf-8'?>
<ArchivedDrafts>
<ScrDraft guid='oldie'>
<Books>
<ScrBook guid='16525edd-c902-43ad-99fa-decb7b751c5d'>
<CanonicalNum val='42' />
</ScrBook>
</Books>
</ScrDraft>
</ArchivedDrafts>";
			var ourContent = commonAncestor.Replace("val='42'", "val='43'");
			var theirContent = commonAncestor.Replace("val='42'", "val='44'");

			var result = FieldWorksTestServices.DoMerge(
				FileHandler,
				_ourFile, ourContent,
				_commonFile, commonAncestor,
				_theirFile, theirContent,
				null, null,
				0, new List<Type>(),
				0, new List<Type>());

			Assert.IsTrue(result.Contains("val=\"42\""));
		}
	}
}