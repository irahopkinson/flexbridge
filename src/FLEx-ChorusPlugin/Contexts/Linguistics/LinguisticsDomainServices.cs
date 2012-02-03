﻿using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FLEx_ChorusPlugin.Contexts.Linguistics.Discourse;
using FLEx_ChorusPlugin.Contexts.Linguistics.Lexicon;
using FLEx_ChorusPlugin.Contexts.Linguistics.Reversals;
using FLEx_ChorusPlugin.Contexts.Linguistics.TextCorpus;
using FLEx_ChorusPlugin.Contexts.Linguistics.WordformInventory;
using FLEx_ChorusPlugin.Infrastructure;
using FLEx_ChorusPlugin.Infrastructure.DomainServices;

namespace FLEx_ChorusPlugin.Contexts.Linguistics
{
	/// <summary>
	/// This domain services class interacts with the Linguistics bounded contexts.
	/// </summary>
	internal static class LinguisticsDomainServices
	{
		internal static void WriteNestedDomainData(string rootDir,
			MetadataCache mdc,
			IDictionary<string, SortedDictionary<string, XElement>> classData,
			Dictionary<string, string> guidToClassMapping,
			HashSet<string> skipWriteEmptyClassFiles)
		{
			var linguisticsBaseDir = Path.Combine(rootDir, SharedConstants.Linguistics);
			if (!Directory.Exists(linguisticsBaseDir))
				Directory.CreateDirectory(linguisticsBaseDir);

			ReversalBoundedContextService.NestContext(linguisticsBaseDir, classData, guidToClassMapping, skipWriteEmptyClassFiles);
			LexiconBoundedContextService.NestContext(linguisticsBaseDir, classData, guidToClassMapping, skipWriteEmptyClassFiles);
			TextCorpusBoundedContextService.NestContext(linguisticsBaseDir, classData, guidToClassMapping, skipWriteEmptyClassFiles);

			// TODO: Switch to proper location.
			var multiFileDirRoot = Path.Combine(rootDir, "DataFiles");
			if (!Directory.Exists(multiFileDirRoot))
				Directory.CreateDirectory(multiFileDirRoot);

			DiscourseAnalysisBoundedContextService.ExtractBoundedContexts(multiFileDirRoot, mdc, classData, guidToClassMapping, skipWriteEmptyClassFiles);
			WordformInventoryBoundedContextService.ExtractBoundedContexts(multiFileDirRoot, mdc, classData, guidToClassMapping, skipWriteEmptyClassFiles);
			PunctuationFormBoundedContextService.ExtractBoundedContexts(multiFileDirRoot, mdc, classData, guidToClassMapping, skipWriteEmptyClassFiles);
			LinguisticsBoundedContextService.ExtractBoundedContexts(multiFileDirRoot, mdc, classData, guidToClassMapping, skipWriteEmptyClassFiles);

			/*
			// Handle the LP TranslationTags prop (OA-CmPossibilityList), if it exists.
			// This goes into TextCorpus folder.
			var translationTagsProp = languageProjectElement.Element("TranslationTags");
			if (translationTagsProp != null)
			{
				var translationTagsObjSurElement = translationTagsProp.Element(SharedConstants.Objsur);
				if (translationTagsObjSurElement != null)
				{
					var tranTagListGuid = translationTagsObjSurElement.Attribute(SharedConstants.GuidStr).Value;
					var className = guidToClassMapping[tranTagListGuid];
					var tranTagList = classData[className][tranTagListGuid];

					CmObjectNestingService.NestObject(tranTagList,
						new Dictionary<string, HashSet<string>>(),
						classData,
						interestingPropertiesCache,
						guidToClassMapping);
					// Remove 'ownerguid'.
					tranTagList.Attribute(SharedConstants.OwnerGuid).Remove();
					var listDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
						new XElement("TranslationTags", tranTagList));
					FileWriterService.WriteNestedFile(Path.Combine(scriptureBaseDir, "TranslationTags." + SharedConstants.List), readerSettings, listDoc);
					languageProjectElement.Element("TranslationTags").RemoveNodes();
				}
			}
			*/
		}

		internal static void FlattenDomain(
			SortedDictionary<string, XElement> highLevelData,
			SortedDictionary<string, XElement> sortedData,
			string rootDir)
		{
			var linguisticsBaseDir = Path.Combine(rootDir, SharedConstants.Linguistics);
			if (!Directory.Exists(linguisticsBaseDir))
				return;

			// Do in reverse order from nesting.
			TextCorpusBoundedContextService.FlattenContext(highLevelData, sortedData, linguisticsBaseDir);
			LexiconBoundedContextService.FlattenContext(highLevelData, sortedData, linguisticsBaseDir);
			ReversalBoundedContextService.FlattenContext(highLevelData, sortedData, linguisticsBaseDir);

			/* Currently handled by BaseDomainServices.
			// TODO: Switch to right location.
			var multiFileDirRoot = Path.Combine(rootDir, "DataFiles");
			DiscourseAnalysisBoundedContextService.RestoreOriginalFile(writer, readerSettings, multiFileDirRoot);
			WordformInventoryBoundedContextService.RestoreOriginalFile(writer, readerSettings, multiFileDirRoot);
			PunctuationFormBoundedContextService.RestoreOriginalFile(writer, readerSettings, multiFileDirRoot);
			LinguisticsBoundedContextService.RestoreOriginalFile(writer, readerSettings, multiFileDirRoot);
			*/
		}

		internal static void RemoveBoundedContextData(string pathRoot)
		{
			var linguisticsBaseDir = Path.Combine(pathRoot, SharedConstants.Linguistics);
			if (!Directory.Exists(linguisticsBaseDir))
				return;

			// Order is less a concern here.
			ReversalBoundedContextService.RemoveBoundedContextData(linguisticsBaseDir);
			LexiconBoundedContextService.RemoveBoundedContextData(linguisticsBaseDir);
			TextCorpusBoundedContextService.RemoveBoundedContextData(linguisticsBaseDir);

			//DiscourseAnalysisBoundedContextService.ExtractBoundedContexts(readerSettings, multiFileDirRoot, mdc, classData, guidToClassMapping, skipwriteEmptyClassFiles);
			//WordformInventoryBoundedContextService.ExtractBoundedContexts(readerSettings, multiFileDirRoot, mdc, classData, guidToClassMapping, skipwriteEmptyClassFiles);
			//PunctuationFormBoundedContextService.ExtractBoundedContexts(readerSettings, multiFileDirRoot, mdc, classData, guidToClassMapping, skipwriteEmptyClassFiles);
			//LinguisticsBoundedContextService.ExtractBoundedContexts(readerSettings, multiFileDirRoot, mdc, classData, guidToClassMapping, skipwriteEmptyClassFiles);

			FileWriterService.RemoveEmptyFolders(linguisticsBaseDir, true);
		}
	}
}