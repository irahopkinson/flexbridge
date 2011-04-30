﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chorus.FileTypeHanders.FieldWorks;
using Chorus.Utilities;
using Palaso.Xml;

namespace FieldWorksBridge.Infrastructure
{
	/// <summary>
	/// Service that will manage the multiple files and original fwdata file for a full FW data set.
	/// </summary>
	/// <remarks>
	/// The task of the service is twofold:
	/// 1. Break up the main fwdata file into multiple files
	///		A. one for the custom property declarations, and
	///		B. one for each concrete CmObject class instance
	/// 2. Put the multiple files back together into the main fwdata file,
	///		but only if a Send/Receive had new information brought back into the local repo.
	///		NB: The client of the service decides if new information was found, and decides to call the service, or not.
	/// </remarks>
	internal static class MultipleFileServices
	{
		private static readonly Encoding Utf8 = Encoding.UTF8;
		private const string FirstElementTag = "AdditionalFields";
		private const string StartTag = "rt";
		/*
		<languageproject version="7000037">
		</languageproject>
		root\DataFiles\ZPI.CustomProperties
		root\DataFiles\ZPI.ModelVersion
		root\DataFiles\ClassName.ClassData
		*/

		internal static void BreakupMainFile(string mainFilePathname, string projectName)
		{
			CheckPathname(mainFilePathname);

			var pathRoot = Path.GetDirectoryName(mainFilePathname);
// ReSharper disable AssignNullToNotNullAttribute
			var multiFileDirRoot = Path.Combine(pathRoot, "DataFiles");
// ReSharper restore AssignNullToNotNullAttribute
			if (!Directory.Exists(multiFileDirRoot))
				Directory.CreateDirectory(multiFileDirRoot);
			else
			{
				// Brutal, but effective in making sure deleted stuff doesn't come back to life in the Restore code. :-)
				foreach (var oldPathname in Directory.GetFiles(multiFileDirRoot, "*.*"))
					File.Delete(oldPathname);
			}

			var mdc = new MetadataCache();

			// Outer Dict has the class name for its key and a sorted (by guid) dictionary as its value.
			// The inner dictionary has a caseless guid as the key and the byte array as the value.
			var classData = new Dictionary<string, SortedDictionary<string, byte[]>>(200, StringComparer.OrdinalIgnoreCase);
			byte[] optionalFirstElement = null;
			using (var fastSplitter = new FastXmlElementSplitter(mainFilePathname))
			{
				bool foundOptionalFirstElement;
				// NB: The main input file *does* have to deal with the optional first element.
				foreach (var record in fastSplitter.GetSecondLevelElementBytes(FirstElementTag, StartTag, out foundOptionalFirstElement))
				{
					if (foundOptionalFirstElement)
					{
						// Cache custom prop file for later write.
						var cpElement = SortCustomPropertiesRecord(record);
						optionalFirstElement = Utf8.GetBytes(cpElement.ToString());
						// Add custom property info to MDC.
						foreach (var propElement in cpElement.Elements("CustomField"))
						{
// ReSharper disable PossibleNullReferenceException
							mdc.AddCustomPropInfo(
								propElement.Attribute("class").Value,
								new FdoPropertyInfo(
									propElement.Attribute("name").Value,
									propElement.Attribute("type").Value));
// ReSharper restore PossibleNullReferenceException
						}
						foundOptionalFirstElement = false;
					}
					else
					{
						CacheDataRecord(mdc, classData, record);
					}
				}
			}

			// Get the 'version' attr value from main file.
			string version;
			using (var reader = XmlReader.Create(mainFilePathname, new XmlReaderSettings {IgnoreWhitespace = true}))
			{
				reader.MoveToContent();
				reader.MoveToAttribute("version");
				version = reader.Value;
			}

			// Write version number file.
			File.WriteAllText(Path.Combine(multiFileDirRoot, projectName + ".ModelVersion"), "{\"modelversion\": " + version + "}");

			var readerSettings = new XmlReaderSettings { IgnoreWhitespace = true };
			// Write optional first element.
			if (optionalFirstElement != null)
				WriteCustomPropertyFile(Path.Combine(multiFileDirRoot, projectName + ".CustomProperties"), readerSettings, optionalFirstElement);

			// Write data records in guid sorted order.
			var highVolumeClasses = new HashSet<string> {"Segment", "WfiAnalysis", "WfiMorphBundle"};
			foreach (var kvp in classData)
			{
				var className = kvp.Key;
				if (highVolumeClasses.Contains(className))
				{
					// Write 10 files for each high volume class.
					WriteSecondaryFiles(multiFileDirRoot, className, readerSettings, kvp.Value);
				}
				else
				{
					// Only write one file.
					WriteSecondaryFile(Path.Combine(multiFileDirRoot, className + ".ClassData"), readerSettings, kvp.Value);
				}
			}
		}

		private static XElement SortCustomPropertiesRecord(byte[] optionalFirstElement)
		{
			var customPropertiesElement = XElement.Parse(Utf8.GetString(optionalFirstElement));

			// <CustomField name="Certified" class="WfiWordform" type="Boolean" />

			// 1. Sort child elements by using a compound key of 'class'+'name'.
			var sortedProperties = new SortedDictionary<string, XElement>();
			foreach (var customProperty in customPropertiesElement.Elements())
			{
// ReSharper disable PossibleNullReferenceException
				// Needs to add 'key' attr, which is class+name, so fast splitter has one id attr to use in its work.
				customProperty.Add(new XAttribute("key", customProperty.Attribute("class").Value + customProperty.Attribute("name").Value));
				sortedProperties.Add(customProperty.Attribute("key").Value, customProperty);
// ReSharper restore PossibleNullReferenceException
			}
			customPropertiesElement.Elements().Remove();
			foreach (var propertyKvp in sortedProperties)
				customPropertiesElement.Add(propertyKvp.Value);

			// Sort all attributes.
			SortAttributes(customPropertiesElement);

			return customPropertiesElement;
		}

		private static void SortAttributes(XElement element)
		{
			if (element.HasElements)
			{
				foreach (var childElement in element.Elements())
					SortAttributes(childElement);
			}

			if (!element.HasAttributes || element.Attributes().Count() <= 1)
				return;

			var sortedAttributes = new SortedDictionary<string, XAttribute>();
			foreach (var attr in element.Attributes())
				sortedAttributes.Add(attr.Name.LocalName, attr);

			element.Attributes().Remove();
			foreach (var sortedAttrKvp in sortedAttributes)
				element.Add(sortedAttrKvp.Value);
		}

		private static void WriteSecondaryFiles(string multiFileDirRoot, string className, XmlReaderSettings readerSettings, SortedDictionary<string, byte[]> data)
		{
			// Divide 'data' into the 10 zero-based buckets.
			var bucket0 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket1 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket2 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket3 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket4 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket5 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket6 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket7 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket8 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			var bucket9 = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

			foreach (var kvp in data)
			{
				var key = kvp.Key;
				var bucket = (int)((uint)new Guid(key).GetHashCode() % 10);
				SortedDictionary<string, byte[]> currentBucket;
				switch(bucket)
				{
					default:
						throw new InvalidOperationException("Bucket not recognized.");
					case 0:
						currentBucket = bucket0;
						break;
					case 1:
						currentBucket = bucket1;
						break;
					case 2:
						currentBucket = bucket2;
						break;
					case 3:
						currentBucket = bucket3;
						break;
					case 4:
						currentBucket = bucket4;
						break;
					case 5:
						currentBucket = bucket5;
						break;
					case 6:
						currentBucket = bucket6;
						break;
					case 7:
						currentBucket = bucket7;
						break;
					case 8:
						currentBucket = bucket8;
						break;
					case 9:
						currentBucket = bucket9;
						break;
				}
				currentBucket.Add(key, kvp.Value);
			}

			// Write out each bucket (another SortedDictionary) using regular WriteSecondaryFile method.
			var basePath = Path.Combine(multiFileDirRoot, className);
			WriteSecondaryFile(basePath + "_01.ClassData", readerSettings, bucket0); // 1-based files vs 0-based buckets.
			WriteSecondaryFile(basePath + "_02.ClassData", readerSettings, bucket1);
			WriteSecondaryFile(basePath + "_03.ClassData", readerSettings, bucket2);
			WriteSecondaryFile(basePath + "_04.ClassData", readerSettings, bucket3);
			WriteSecondaryFile(basePath + "_05.ClassData", readerSettings, bucket4);
			WriteSecondaryFile(basePath + "_06.ClassData", readerSettings, bucket5);
			WriteSecondaryFile(basePath + "_07.ClassData", readerSettings, bucket6);
			WriteSecondaryFile(basePath + "_08.ClassData", readerSettings, bucket7);
			WriteSecondaryFile(basePath + "_09.ClassData", readerSettings, bucket8);
			WriteSecondaryFile(basePath + "_10.ClassData", readerSettings, bucket9);
		}

		private static void WriteSecondaryFile(string newPathname, XmlReaderSettings readerSettings, SortedDictionary<string, byte[]> data)
		{
			if (File.Exists(newPathname))
				File.Delete(newPathname);
			using (var writer = XmlWriter.Create(newPathname, CanonicalXmlSettings.CreateXmlWriterSettings()))
			{
				writer.WriteStartElement("classdata");
				foreach (var kvp in data)
					WriteElement(writer, readerSettings, kvp.Value);
				writer.WriteEndElement();
			}
		}

		private static void WriteCustomPropertyFile(string newPathname, XmlReaderSettings readerSettings, byte[] element)
		{
			if (File.Exists(newPathname))
				File.Delete(newPathname);
			using (var writer = XmlWriter.Create(newPathname, CanonicalXmlSettings.CreateXmlWriterSettings()))
				WriteElement(writer, readerSettings, element);
		}

		private static void WriteElement(XmlWriter writer, XmlReaderSettings readerSettings, byte[] optionalFirstElement)
		{
			using (var nodeReader = XmlReader.Create(new MemoryStream(optionalFirstElement, false), readerSettings))
				writer.WriteNode(nodeReader, true);
		}

		internal static void RestoreMainFile(string mainFilePathname, string projectName)
		{
			CheckPathname(mainFilePathname);

			var pathRoot = Path.GetDirectoryName(mainFilePathname);
// ReSharper disable AssignNullToNotNullAttribute
			var multiFileDirRoot = Path.Combine(pathRoot, "DataFiles");
// ReSharper restore AssignNullToNotNullAttribute

			var tempPathname = Path.GetTempFileName();

			try
			{
				// There is no particular reason to ensure the order of objects in 'mainFilePathname' is retained,
				// but the optional custom props element must be first.

				var readerSettings = new XmlReaderSettings { IgnoreWhitespace = true };
				// NB: This should follow current FW write settings practice.
				var fwWriterSettings = new XmlWriterSettings
				{
					OmitXmlDeclaration = false,
					CheckCharacters = true,
					ConformanceLevel = ConformanceLevel.Document,
					Encoding = new UTF8Encoding(false),
					Indent = true,
					IndentChars = (""),
					NewLineOnAttributes = false
				};

				using (var writer = XmlWriter.Create(tempPathname, fwWriterSettings))
				{
					writer.WriteStartElement("languageproject");

					// Write out version number from the ModelVersion file.
					var modelVersionData = File.ReadAllText(Path.Combine(multiFileDirRoot, projectName + ".ModelVersion"));
					var splitModelVersionData = modelVersionData.Split(new[] { "{", ":", "}" }, StringSplitOptions.RemoveEmptyEntries);
					writer.WriteAttributeString("version", splitModelVersionData[1].Trim());

					// Write out optional custom property file.
					var optionalCustomPropFile = Path.Combine(multiFileDirRoot, projectName + ".CustomProperties");
					if (File.Exists(optionalCustomPropFile))
					{
						// Remove 'key' attribute from CustomField elements.
						var doc = XDocument.Load(optionalCustomPropFile);
						foreach (var cf in doc.Descendants("CustomField"))
							cf.Attribute("key").Remove();
						using (var reader = XmlReader.Create(optionalCustomPropFile, readerSettings))
						{
							reader.MoveToContent();
							writer.WriteNode(reader, false);
						}
					}

					// Work on all class data files.
					foreach (var pathname in Directory.GetFiles(multiFileDirRoot, "*.ClassData"))
					{
						using (var reader = XmlReader.Create(pathname, readerSettings))
						{
							reader.MoveToContent();
							reader.Read();
							while (reader.IsStartElement())
								writer.WriteNode(reader, false);
						}
					}
					writer.WriteEndElement();
				}

				File.Copy(tempPathname, mainFilePathname, true);
			}
			finally
			{
				if (File.Exists(tempPathname))
					File.Delete(tempPathname);
			}
		}

		private static void CheckPathname(string mainFilePathname)
		{
			// Just because all of this is true, doesn't mean it is a FW 7.0 related file. :-(
			if (!string.IsNullOrEmpty(mainFilePathname) // No null or empty string can be valid.
				&& File.Exists(mainFilePathname) // There has to be an actual file,
				&& Path.GetExtension(mainFilePathname).ToLowerInvariant() == ".fwdata")
				return;

			throw new ApplicationException("Cannot process the given file.");
		}

		private static void CacheDataRecord(MetadataCache mdc, IDictionary<string, SortedDictionary<string, byte[]>> classData, byte[] record)
		{
			var rtElement = XElement.Parse(Utf8.GetString(record));
// ReSharper disable PossibleNullReferenceException
			var className = rtElement.Attribute("class").Value;
			var guid = rtElement.Attribute("guid").Value;
// ReSharper restore PossibleNullReferenceException

			// 1. Remove 'Checksum' from wordforms.
			if (className == "WfiWordform")
			{
				var csElement = rtElement.Element("Checksum");
				if (csElement != null)
					csElement.Remove();
			}

			// Get collection propeties for the class.
			var colPropNames = (from pi in mdc.GetClassInfo(className).AllCollectionProperties
							   select pi.PropertyName).ToList();

			// 2. Sort property elements of <rt>
			var sortedPropertyElements = new SortedDictionary<string, XElement>();
			foreach (var propertyElement in rtElement.Elements())
			{
				var propName = propertyElement.Name.LocalName;
				// <Custom name="Certified" val="True" />
// ReSharper disable PossibleNullReferenceException
				if (propName == "Custom")
					propName = propertyElement.Attribute("name").Value; // Sort custom props by their name attrs.
// ReSharper restore PossibleNullReferenceException
				if (colPropNames.Contains(propName))
					SortCollectionProperties(propertyElement);
				sortedPropertyElements.Add(propName, propertyElement);
			}
			rtElement.Elements().Remove();
			foreach (var kvp in sortedPropertyElements)
				rtElement.Add(kvp.Value);

			// 3. Sort attributes at all levels.
			SortAttributes(rtElement);

			SortedDictionary<string, byte[]> recordData;
			if (!classData.TryGetValue(className, out recordData))
			{
				recordData = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
				classData.Add(className, recordData);
			}
			recordData.Add(guid, Utf8.GetBytes(rtElement.ToString()));
		}

		private static void SortCollectionProperties(XElement propertyElement)
		{
			// Write collection properties in guid sorted order,
			// since order is not significant in collections,
			// but it will  be easier on Hg.
			var sortCollectionData = new SortedDictionary<string, XElement>();
			foreach (var objsurElement in propertyElement.Elements("objsur"))
			{
// ReSharper disable PossibleNullReferenceException
				sortCollectionData.Add(objsurElement.Attribute("guid").Value, objsurElement);
// ReSharper restore PossibleNullReferenceException
			}
			if (sortCollectionData.Count > 1)
			{
				propertyElement.Elements().Remove();
				foreach (var kvp in sortCollectionData)
					propertyElement.Add(kvp.Value);
			}
		}
	}
}
