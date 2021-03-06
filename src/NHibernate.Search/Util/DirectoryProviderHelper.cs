using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Lucene.Net.Store;
using NHibernate.Search.Impl;
using NHibernate.Util;

namespace NHibernate.Search
{
    public class DirectoryProviderHelper
    {
		private static readonly IInternalLogger log = LoggerProvider.LoggerFor(typeof(DirectoryProviderHelper));

        /// <summary>
        /// Build a directory name out of a root and relative path, guessing the significant part
        /// and checking for the file availability
        /// </summary>
        public static string GetSourceDirectory(string rootPropertyName, string relativePropertyName, string directoryProviderName, IDictionary properties)
        {
            // TODO check that it's a directory
            string root = (string) properties[rootPropertyName];
            string relative = (string) properties[relativePropertyName];
            if (log.IsDebugEnabled)
            {
                log.Debug(
                        "Guess source directory from " + rootPropertyName + " " + root != null
                                ? root
                                : "<null>" + " and " + relativePropertyName + " " + (relative ?? "<null>"));
            }

            if (relative == null)
            {
                relative = directoryProviderName;
            }

            if (StringHelper.IsEmpty(root))
            {
                log.Debug("No root directory, go with relative " + relative);
                DirectoryInfo sourceFile = new DirectoryInfo(relative);
                if (!sourceFile.Exists)
                {
                    throw new HibernateException("Unable to read source directory: " + relative);
                }
                //else keep source as it
            }
            else
            {
                DirectoryInfo rootDir = new DirectoryInfo(root);
                if (!rootDir.Exists)
                {
                    try
                    {
                        rootDir.Create();
                        rootDir = new DirectoryInfo(root);
                    }
                    catch (IOException e)
                    {
                        throw new SearchException(root + " does not exist and cannot be created", e);
                    }
                }

                // Test again in case Create failed for wrong reasons
                if (rootDir.Exists)
                {
                    DirectoryInfo sourceFile = new DirectoryInfo(Path.Combine(root, relative));
                    if (!sourceFile.Exists)
                    {
                        sourceFile.Create();
                    }

                    log.Debug("Get directory from root + relative");
                    try
                    {
                        relative = sourceFile.FullName;
                    }
                    catch (IOException)
                    {
                        throw new AssertionFailure("Unable to get canonical path: " + root + " + " + relative);
                    }
                }
                else
                {
                    throw new SearchException(root + " does not exist");
                }
            }

            return relative;
        }

        public static DirectoryInfo DetermineIndexDir(String directoryProviderName, IDictionary properties)
        {
            string indexBase = (string) properties["indexBase"] ?? ".";
            string indexName = (string) properties["indexName"] ?? directoryProviderName;

            // We need this to allow using the search from the web, where the "." directory is somewhere in the system root.
            indexBase = indexBase.Replace("~", AppDomain.CurrentDomain.BaseDirectory);
            DirectoryInfo indexDir = new DirectoryInfo(indexBase);
            if (!indexDir.Exists)
            {
                // if the base directory does not exist, create it
                indexDir.Create();
            }

            if (!HasWriteAccess(indexDir))
            {
                throw new HibernateException("Cannot write into index directory: " + indexBase);
            }

            indexDir = new DirectoryInfo(Path.Combine(indexDir.FullName, indexName));
            return indexDir;
        }

        /// <summary>
        /// Creates a LockFactory as selected in the configuration for the DirectoryProvider.
        /// The SimpleFSLockFactory and NativeFSLockFactory need a File to know
        /// where to stock the filesystem based locks; other implementations ignore this parameter.
        /// </summary>
        /// <param name="indexDir">the directory to use to store locks, if needed by implementation</param>
        /// <param name="properties">the configuration of current DirectoryProvider</param>
        /// <returns>the LockFactory as configured, or a SimpleFSLockFactory in case of configuration errors or as a default.</returns>
        public static LockFactory CreateLockFactory(DirectoryInfo indexDir, IDictionary<string, string> dirConfiguration)
        {
            string lockFactoryName;
            if (dirConfiguration.ContainsKey("locking_strategy")) lockFactoryName = dirConfiguration["locking_strategy"];
            else lockFactoryName = "simple";
            switch (lockFactoryName)
            {
                case "simple":
                    return new SimpleFSLockFactory(indexDir);
                case "native":
                    return new NativeFSLockFactory(indexDir);
                case "single":
                    return new SingleInstanceLockFactory();
                case "none":
                    return new NoLockFactory();
                default:
                    string message = string.Format("Invalid configuration setting for option locking_strategy \"{0}\"; option ignored!", lockFactoryName);
                    log.Warn(message);
                    return new SimpleFSLockFactory(indexDir);
            }
        }

        private static bool HasWriteAccess(DirectoryInfo indexDir)
        {
            string tempFileName = Path.Combine(indexDir.FullName, Guid.NewGuid().ToString());

            // Yuck! but it is the simplest way
            try
            {
                File.CreateText(tempFileName).Close();
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            try
            {
                File.Delete(tempFileName);
            }
            catch (UnauthorizedAccessException)
            {
                // We may have permissions to create but not delete, ignoring
            }

            return true;
        }
    }
}