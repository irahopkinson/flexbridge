﻿using System;
using System.IO;
using FieldWorksBridge.Model;
using NUnit.Framework;

namespace FieldWorksBridgeTests.Model
{
	/// <summary>
	/// Test the LanguageProject class.
	/// </summary>
	[TestFixture]
	public class LanguageProjectTests
	{
		[Test, ExpectedException(typeof(ArgumentNullException))]
		public void NullPathnameThrows()
		{
			new LanguageProject(null);
		}

		[Test, ExpectedException(typeof(ArgumentNullException))]
		public void EmptyPathnameThrows()
		{
			new LanguageProject(string.Empty);
		}

		[Test, ExpectedException(typeof(FileNotFoundException))]
		public void NonExistantFileThrows()
		{
			new LanguageProject("NobodyHome");
		}

		[Test, ExpectedException(typeof(ArgumentException))]
		public void NonFwFileThrows()
		{
			var tempFile = Path.GetTempFileName();
			try
			{
				new LanguageProject(tempFile);
			}
			finally
			{
				File.Delete(tempFile);
			}
		}

		[Test]
		public void FwFileHasFolderPath()
		{
			var temp = Path.GetTempFileName();
			var tempFile = Path.ChangeExtension(temp, ".fwdata");
			File.Move(temp, tempFile);
			try
			{
				var lp = new LanguageProject(tempFile);
				Assert.AreEqual(Path.GetDirectoryName(tempFile), lp.DirectoryName);
			}
			finally
			{
				File.Delete(tempFile);
			}
		}
	}
}