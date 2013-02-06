using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using BlogEngine.Core;
using BlogEngine.Core.Web.Controls;
using BlogEngine.Core.Web.Extensions;

namespace Brafton.BlogEngine
{
    [Extension("Imports articles from Brafton, ContentLEAD, and Castleford XML feeds.", "0.3", "<a href=\"http://contentlead.com/\">ContentLEAD</a>")]
    class BraftonArticleImporter
    {
        protected ExtensionSettings _settings;
        private static StreamWriter _logStream;
        private static object _logLock;

        public BraftonArticleImporter()
        {
            Initialize();
            Post.Serving += new EventHandler<ServingEventArgs>(Post_Serving);
        }

        void Post_Serving(object sender, ServingEventArgs e)
        {
            DateTime lastUpload = DateTime.Parse(_settings.GetSingleValue("LastUpload"));
            int interval = Int32.Parse(_settings.GetSingleValue("Interval"));

            if (DateTime.Now > lastUpload + TimeSpan.FromMinutes((double)interval))
            {
                try
                {
                    _settings.UpdateScalarValue("LastUpload", DateTime.Now.ToUniversalTime().ToString("u"));
                    Import();
                }
                catch (Exception ex)
                {
                    Log(string.Format("FATAL: Uncaught exception: {0}", ex.ToString()), LogLevel.Critical);
                }

                if (_logStream != null)
                {
                    _logStream.Close();
                    _logStream = null;
                    _logLock = null;
                }
            }
        }

        protected void Import()
        {
            if (string.IsNullOrEmpty(_settings.GetSingleValue("ApiKey")))
            {
                Log("API key not set. Stopping.", LogLevel.Notice);
                return;
            }

            string apiKey = _settings.GetSingleValue("ApiKey");
            string baseUrl = _settings.GetSingleValue("BaseUrl");

            Uri baseUri = new Uri(baseUrl);
            Uri feedUri = new Uri(baseUri, apiKey);

            Log(string.Format("Starting import at feed URI '{0}'.", feedUri.ToString()), LogLevel.Debug);
            ApiContext api = new ApiContext(apiKey, baseUrl);

            DateTime startTime = DateTime.Now;

            foreach (newsItem ni in api.News)
            {
                Post p = FindPost(ni);

                if (p == null)
                {
                    Log(string.Format("Importing new post '{0}' (ID {1}).", ni.headline.Trim(), ni.id), LogLevel.Info);
                    p = ConvertToPost(ni);

                    Category[] categories = GetNewsItemCategories(ni);
                    foreach (Category c in categories)
                    {
                        Category ca = FindCategory(c);
                        if (ca == null)
                        {
                            if (c.Save() == SaveAction.Insert)
                            {
                                Log(string.Format("Imported new category '{0}'.", c.Title.Trim()), LogLevel.Info);
                                p.Categories.Add(c);
                            }
                        }
                        else
                            p.Categories.Add(ca);
                    }

                    // it should noted that, technically, the xml can specify more than one photo.
                    PhotoInstance? thumbnail = GetPhotoInstance(ni, new enumeratedTypes.enumPhotoInstanceType[] { enumeratedTypes.enumPhotoInstanceType.Thumbnail, enumeratedTypes.enumPhotoInstanceType.Small });
                    PhotoInstance? fullSizePhoto = GetPhotoInstance(ni, enumeratedTypes.enumPhotoInstanceType.Large);

                    if (thumbnail != null || fullSizePhoto != null)
                    {
                        string thumbnailPath = string.Empty;
                        string fullSizePhotoPath = string.Empty;
                        int photoImportCount = 0;

                        string physicalPicsPath = HttpContext.Current.Server.MapPath(Path.Combine(Blog.CurrentInstance.VirtualPath, "pics"));
                        string fullSizePhysicalPath = string.Empty;
                        string thumbnailPhysicalPath = string.Empty;
                        string webrootUri = global::BlogEngine.Core.Blog.CurrentInstance.AbsoluteWebRoot.ToString();
                        string webrootAuthorityUri = global::BlogEngine.Core.Blog.CurrentInstance.AbsoluteWebRootAuthority.ToString();
                        string picsUri = webrootUri.Substring(webrootUri.IndexOf(webrootAuthorityUri) + webrootAuthorityUri.Length) + "pics/";

                        if (fullSizePhoto != null && ImportPhoto(fullSizePhoto, physicalPicsPath, out fullSizePhysicalPath))
                        {
                            photoImportCount++;
                            p.Content = AppendImageToContent(fullSizePhoto.Value, picsUri + Path.GetFileName(fullSizePhoto.Value.Url), p.Content, "article-img-frame", "", true);
                        }

                        if (thumbnail != null && ImportPhoto(thumbnail, physicalPicsPath, out thumbnailPhysicalPath))
                        {
                            photoImportCount++;
                            p.Description = AppendImageToContent(thumbnail.Value, picsUri + Path.GetFileName(thumbnail.Value.Url), p.Description, "article-thumbnail-frame");
                        }

                        if (photoImportCount > 0)
                            Log(string.Format("Imported {0} photo{1}.", photoImportCount, (photoImportCount != 1 ? "s" : "")), LogLevel.Info);
                        else
                            Log("Warning: could not import any photos.", LogLevel.Warning);
                    }

                    if (!p.Valid)
                    {
                        Log(string.Format("Error: post '{0}' invalid: '{1}'", p.ValidationMessage), LogLevel.Error);
                        continue;
                    }
                    else
                        p.Save();
                }
            }
            Post.Reload();

            DateTime endTime = DateTime.Now;
            TimeSpan elapsed = endTime - startTime;

            Log(string.Format("Import finished; took {0}.", elapsed.ToString()), LogLevel.Debug);
        }

        private string AppendImageToContent(PhotoInstance photoInstance, string virtualPath, string content, string cssClass, string imageLink, bool useCaption)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(string.Format("<div class=\"{0}\">", cssClass));
            if (!string.IsNullOrEmpty(imageLink))
                sb.Append(string.Format("<a href=\"{0}\">", imageLink));
            sb.Append(string.Format("<img src=\"{0}\" alt=\"{1}\" />", virtualPath, photoInstance.AltText));
            if (!string.IsNullOrEmpty(imageLink))
                sb.Append("</a>");
            if (useCaption)
                sb.Append(string.Format("<span class=\"caption\">{0}</span>", photoInstance.Caption));
            sb.Append("</div>");
            sb.Append(content);

            return sb.ToString();
        }

        private string AppendImageToContent(PhotoInstance photoInstance, string virtualPath, string content, string cssClass)
        {
            return AppendImageToContent(photoInstance, virtualPath, content, cssClass, "", false);
        }

        private bool ImportPhoto(PhotoInstance? photoInstance, string picsPath, out string physicalPhotoPath)
        {
            string filename = Path.GetFileName(photoInstance.Value.Url);
            string physicalPath = Path.Combine(picsPath, filename);
            using (WebClient wc = new WebClient())
            {
                try
                {
                    wc.DownloadFile(photoInstance.Value.Url, physicalPath);
                }
                catch (Exception ex)
                {
                    Log(string.Format("Error: Could not import photo '{0}': {1}", photoInstance.Value.Url, ex.Message), LogLevel.Error);
                    physicalPhotoPath = string.Empty;
                    return false;
                }
            }

            physicalPhotoPath = physicalPath;
            return true;
        }

        private Category FindCategory(Category c)
        {
            return Category.Categories.Find(ca => ca.Title.Trim() == c.Title.Trim());
        }

        private Category[] GetNewsItemCategories(newsItem ni)
        {
            List<Category> cats = new List<Category>();
            IEnumerator<category> catEn = ni.categories.GetEnumerator();
            while (catEn.MoveNext())
                cats.Add(new Category(catEn.Current.name, ""));

            return cats.ToArray();
        }

        private PhotoInstance? GetPhotoInstance(newsItem ni, enumeratedTypes.enumPhotoInstanceType photoType)
        {
            return GetPhotoInstance(ni, new enumeratedTypes.enumPhotoInstanceType[] { photoType });
        }

        private PhotoInstance? GetPhotoInstance(newsItem ni, enumeratedTypes.enumPhotoInstanceType[] photoTypes)
        {
            IEnumerator<photo> phEn = ni.photos.GetEnumerator();
            if (!phEn.MoveNext())
                return null;

            PhotoInstance phIns = new PhotoInstance();
            bool found = false;
            photo ph = phEn.Current;
            IEnumerator<photo.Instance> phIEn = ph.Instances.GetEnumerator();
            while (phIEn.MoveNext())
            {
                foreach (enumeratedTypes.enumPhotoInstanceType phType in photoTypes)
                {
                    if (phIEn.Current.type == phType)
                    {
                        phIns.Width = phIEn.Current.width;
                        phIns.Height = phIEn.Current.height;
                        phIns.Url = phIEn.Current.url;
                        phIns.Type = phIEn.Current.type;
                        phIns.Type = phType;

                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    phIns.AltText = phEn.Current.htmlAlt;
                    phIns.Caption = phEn.Current.caption;
                    phIns.Id = phEn.Current.id;
                    phIns.Orientation = phEn.Current.orientation;

                    break;
                }
            }

            if (!found)
                return null;
            return phIns;
        }

        private Post ConvertToPost(newsItem ni)
        {
            Post p = new Post();

            p.Author = "Admin";
            foreach (category c in ni.categories)
                p.Categories.Add(new Category(c.name, ""));
            p.Content = ni.text;

            string importedDate = _settings.GetSingleValue("ImportedDate");
            switch (importedDate)
            {
                case "Published Date":
                    p.DateCreated = ni.publishDate;
                    break;

                case "Last Modified Date":
                    p.DateCreated = ni.lastModifiedDate;
                    break;

                default:
                    p.DateCreated = ni.createdDate;
                    break;
            }
            p.DateModified = ni.lastModifiedDate;
            p.Description = ni.extract;

            Regex alphaNumeric = new Regex("[^a-zA-Z0-9]+");
            p.Slug = alphaNumeric.Replace(ni.headline.ToLower().Trim(), "-");

            if (!string.IsNullOrEmpty(ni.htmlMetaKeywords))
                p.Tags.AddRange(ni.htmlMetaKeywords.Split(new char[] { ',' }));
            p.Title = ni.headline.Trim();

            return p;
        }

        private Post FindPost(newsItem ni)
        {
            return Post.Posts.Find(p => p.Title.Trim() == ni.headline.Trim());
        }

        private void Initialize()
        {
            ExtensionSettings settings = new ExtensionSettings(this);
            settings.IsScalar = true;

            settings.AddParameter("BaseUrl", "Feed Provider");
            string[] providers = { "http://api.brafton.com/", "http://api.contentlead.com/", "http://api.castleford.com.au/" };
            settings.AddValue("BaseUrl", providers, providers[0]);

            settings.AddParameter("ApiKey", "API Key", 36, true);

            settings.AddParameter("Interval", "Upload Interval (minutes)", 6, true);
            settings.AddValue("Interval", 180);

            settings.AddParameter("ImportedDate", "Imported Date");
            settings.SetParameterType("ImportedDate", ParameterType.DropDown);
            string[] sortOptions = { "Published Date", "Created Date", "Last Modified Date" };
            settings.AddValue("ImportedDate", sortOptions, sortOptions[0]);

            settings.AddParameter("LastUpload", "Time of last upload");
            settings.AddValue("LastUpload", DateTime.MinValue.ToString("u"));

            _settings = ExtensionManager.InitSettings("BraftonArticleImporter", settings);
        }

        protected static void Log(string message, LogLevel level)
        {
            if (_logStream == null)
            {
                _logLock = new object();
                string logFilename = "BraftonImporter.log";
                string logFolder = AppDomain.CurrentDomain.GetData("DataDirectory").ToString();
                string logPath = Path.Combine(logFolder, logFilename);

                _logStream = new StreamWriter(logPath, true);
            }

            lock (_logLock)
            {
                _logStream.WriteLine(string.Format("[{0}]\t{1}\t\t{2}", DateTime.Now.ToString("s"), level.ToString(), message));
            }
        }
    }

    public enum LogLevel
    {
        /// <summary>
        /// Critical conditions.
        /// </summary>
        Critical,
        /// <summary>
        /// Debugging information.
        /// </summary>
        Debug,
        /// <summary>
        /// Indicates an error has occurred.
        /// </summary>
        Error,
        /// <summary>
        /// Used for informative messages.
        /// </summary>
        Info,
        /// <summary>
        /// A normal but significant condition.
        /// </summary>
        Notice,
        /// <summary>
        /// A warning condition.
        /// </summary>
        Warning
    }

    struct PhotoInstance
    {
        public int Width, Height, Id;
        public string Url, Caption, AltText;
        public enumeratedTypes.enumPhotoInstanceType Type;
        public enumeratedTypes.enumPhotoOrientation Orientation;
    }
}
