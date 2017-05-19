using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml;

namespace ResolveReferences
{
	static class Program
	{

		private static string pathToSpaceEngineers;
		private static HashSet<string> seAssemblies;
		private static bool disableCopyLocal, removeHintPath;

		private static void Main(string[] args)
		{
			if (args != null)
				foreach (string arg in args)
				{
					if (arg.Equals("--disableCopyLocal", StringComparison.InvariantCultureIgnoreCase))
						disableCopyLocal = true;
					else if (arg.Equals("--removeHintPath", StringComparison.InvariantCultureIgnoreCase))
						removeHintPath = true;
				}

			RegistrySearch();
			if (CheckPath())
			{
				if (disableCopyLocal || removeHintPath)
					seAssemblies = GetSeAssemblies();

				Run();
			}
			else
			{
				Console.Error.WriteLine("Failed to get path to Space Engineers from registry.");
				return;
			}
		}

		private static bool CheckPath()
		{
			if (pathToSpaceEngineers == null || !Directory.Exists(pathToSpaceEngineers))
				return false;

			if (pathToSpaceEngineers.EndsWith("\\"))
				pathToSpaceEngineers = pathToSpaceEngineers.Substring(0, pathToSpaceEngineers.Length - 1);

			if (pathToSpaceEngineers.EndsWith("SpaceEngineers\\Bin64"))
				return true;

			if (pathToSpaceEngineers.EndsWith("SpaceEngineers"))
			{
				string bin64 = Path.Combine(pathToSpaceEngineers, "Bin64");
				if (Directory.Exists(bin64))
				{
					pathToSpaceEngineers = bin64;
					return true;
				}
			}

			return false;
		}

		private static void RegistrySearch()
		{
			const string firstKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 244850";
			const string secondKey = "Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 244850";

			if (RegistrySearch(firstKey))
				return;
			RegistrySearch(secondKey);
		}

		private static bool RegistrySearch(string keyPath)
		{
			using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
				if (key != null)
				{
					object value = key.GetValue("InstallLocation");
					if (value is string)
					{
						Debug.WriteLine("Read registry key: " + keyPath);

						pathToSpaceEngineers = (string)value;
						return true;
					}
				}

			return false;
		}

		private static void Run()
		{
			// find solution file
			foreach (string path in PathsToRoot(Environment.CurrentDirectory))
				foreach (string maybeSolutionFile in Directory.EnumerateFiles(path))
					if (maybeSolutionFile.EndsWith(".sln"))
					{
						string solutionDirectory = Path.GetDirectoryName(maybeSolutionFile);
						Debug.WriteLine("Solution directory: " + solutionDirectory);

						// look for csproj files
						foreach (string filePath in Directory.EnumerateFiles(solutionDirectory, "*", SearchOption.AllDirectories))
						{
							if (!filePath.EndsWith(".csproj"))
								continue;

							SetReferencePath(filePath);
							CsProjUpdate(filePath);
						}

						return;
					}

			Console.Error.WriteLine("Failed to locate solution file");
		}

		private static IEnumerable<string> PathsToRoot(string path)
		{
			if (!Path.IsPathRooted(path))
				throw new ArgumentException("Path is not rooted: " + path);
			string root = Path.GetPathRoot(path);
			while (path != root)
			{
				yield return path;
				path = Path.GetDirectoryName(path);
			}
		}

		private static void SetReferencePath(string projFilePath)
		{
			string userFilePath = projFilePath + ".user";
			XmlDocument userDocument = new XmlDocument();
			if (!File.Exists(userFilePath))
			{
				File.Create(userFilePath).Dispose();
				Console.WriteLine("creating: " + userFilePath);
			}
			else
				try
				{
					userDocument.Load(userFilePath);
					Debug.WriteLine("opened: " + userFilePath);
				}
				catch (XmlException)
				{
					Console.Error.WriteLine("failed to load: " + userFilePath);
					throw;
				}

			SetRoot(userDocument, projFilePath);
			if (SetReferencePath(userDocument))
			{
				Console.WriteLine("Saving " + userFilePath);
				userDocument.Save(userFilePath);

				// VS won't notice a user file created but if the project file is changed, VS will reload the project and user files.
				File.SetLastWriteTimeUtc(projFilePath, DateTime.UtcNow);
			}
		}

		/// <summary>
		/// Set the root for the document.
		/// </summary>
		/// <param name="document">The document to set the root for.</param>
		/// <param name="templateFilePath">csproj to copy xml declarations from</param>
		private static void SetRoot(XmlDocument document, string templateFilePath)
		{
			const string
				toolsVersionName = "ToolsVersion";

			XmlDocument templateDocument = new XmlDocument();
			templateDocument.Load(templateFilePath);
			XmlDeclaration templateDeclaration = (XmlDeclaration)templateDocument.FirstChild;
			XmlElement templateRoot = templateDocument.DocumentElement;

			if (document.FirstChild == null)
				document.AppendChild(document.CreateXmlDeclaration(templateDeclaration.Version, templateDeclaration.Encoding, templateDeclaration.Standalone));

			XmlElement root = document.DocumentElement;
			if (root == null)
			{
				Console.WriteLine("creating document root");
				root = document.CreateElement(templateRoot.Name, templateRoot.NamespaceURI);

				document.AppendChild(root);
				XmlAttribute toolsVersion = document.CreateAttribute(toolsVersionName, templateRoot.GetAttributeNode(toolsVersionName).NamespaceURI);
				root.Attributes.Append(toolsVersion);
				toolsVersion.InnerText = templateRoot.Attributes[toolsVersionName].InnerText;
			}
			else if (root.Name != templateRoot.Name)
				throw new Exception("document root's name(" + root.Name + ") is not " + templateRoot.Name);
		}

		/// <summary>
		/// Set ReferencePath in PropertyGroup to <see cref="pathToSpaceEngineers"/>.
		/// </summary>
		/// <param name="document">Document to set ReferencePath for.</param>
		/// <returns>True iff the document was modified.</returns>
		private static bool SetReferencePath(XmlDocument document)
		{
			const string propGroup = "PropertyGroup", refPath = "ReferencePath";

			foreach (XmlNode propGroupNode in document.DocumentElement.GetElementsByTagName(propGroup))
				foreach (XmlNode propGroupChildNode in propGroupNode.ChildNodes)
					if (propGroupChildNode.Name == refPath)
						if (propGroupChildNode.InnerText == pathToSpaceEngineers)
						{
							Debug.WriteLine("Reference path already set:\n" + pathToSpaceEngineers);
							return false;
						}
						else
						{
							Console.WriteLine("Changing reference from\n" + propGroupChildNode.InnerText + "\nto\n" + pathToSpaceEngineers);
							propGroupChildNode.InnerText = pathToSpaceEngineers;
							return true;
						}

			Console.WriteLine("Adding new reference path");
			XmlElement refPathElement = document.CreateElement(refPath, document.DocumentElement.NamespaceURI);
			refPathElement.InnerText = pathToSpaceEngineers;
			XmlElement propGroupElement = document.CreateElement(propGroup, document.DocumentElement.NamespaceURI);
			propGroupElement.AppendChild(refPathElement);
			document.DocumentElement.AppendChild(propGroupElement);

			return true;
		}

		private static HashSet<string> GetSeAssemblies()
		{
			HashSet<string> assemblyNames = new HashSet<string>();
			foreach (string filePath in Directory.EnumerateFiles(pathToSpaceEngineers))
			{
				string ext = Path.GetExtension(filePath);
				if (ext == ".dll" || ext == ".exe")
				{
					AssemblyName name;
					try { name = AssemblyName.GetAssemblyName(filePath); }
					catch (BadImageFormatException) { continue; }
					assemblyNames.Add(name.FullName);
					assemblyNames.Add(name.Name);
				}
			}
			return assemblyNames;
		}

		private static void CsProjUpdate(string projFilePath)
		{
			if (seAssemblies == null)
				return;

			XmlDocument projDocument = new XmlDocument();
			try
			{
				projDocument.Load(projFilePath);
				Debug.WriteLine("opened: " + projFilePath);
			}
			catch (XmlException)
			{
				Console.Error.WriteLine("failed to load: " + projFilePath);
				throw;
			}

			if (CsProjUpdate(projDocument))
			{
				Console.WriteLine("Saving " + projFilePath);
				projDocument.Save(projFilePath);
			}
		}

		private static bool CsProjUpdate(XmlDocument document)
		{
			bool changed = false;

			Stopwatch watch = new Stopwatch();
			watch.Start();

			List<XmlElement> removals = new List<XmlElement>();

			foreach (XmlElement itemGroupElement in document.DocumentElement.GetElementsByTagName("ItemGroup"))
				foreach (XmlElement referenceElement in itemGroupElement.GetElementsByTagName("Reference"))
					foreach (XmlAttribute attribute in referenceElement.Attributes)
						if (attribute.Name == "Include")
							if (seAssemblies.Contains(attribute.Value))
							{
								if (disableCopyLocal)
									changed = DisableCopyLocal(document, changed, referenceElement, attribute);
								if (removeHintPath)
									changed = RemoveHintPath(changed, removals, referenceElement);
							}

			watch.Stop();
			Console.WriteLine("Update time: " + watch.Elapsed.TotalSeconds);

			return changed;
		}

		private static bool DisableCopyLocal(XmlDocument document, bool changed, XmlElement referenceElement, XmlAttribute attribute)
		{
			bool hasPrivate = false;

			foreach (XmlElement privateElement in referenceElement.GetElementsByTagName("Private"))
			{
				hasPrivate = true;
				if (!privateElement.InnerText.Equals("False", StringComparison.InvariantCultureIgnoreCase))
				{
					changed = true;
					privateElement.InnerText = "False";
					Console.WriteLine("Changed private element for " + attribute.Value);
				}
			}

			if (!hasPrivate)
			{
				changed = true;
				XmlElement privateElement = document.CreateElement("Private", document.DocumentElement.NamespaceURI);
				privateElement.InnerText = "False";
				referenceElement.AppendChild(privateElement);
				Console.WriteLine("Set private element for " + attribute.Value);
			}

			return changed;
		}

		private static bool RemoveHintPath(bool changed, List<XmlElement> removals, XmlElement referenceElement)
		{
			foreach (XmlElement hintPathElement in referenceElement.GetElementsByTagName("HintPath"))
				removals.Add(hintPathElement);
			if (removals.Count != 0)
			{
				changed = true;
				foreach (XmlElement hintPathElement in removals)
					referenceElement.RemoveChild(hintPathElement);
				removals.Clear();
			}

			return changed;
		}
	}
}
