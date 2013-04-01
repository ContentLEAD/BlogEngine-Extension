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
using AdferoVideoDotNet.AdferoArticles;
using AdferoVideoDotNet.AdferoArticlesVideoExtensions;
using AdferoVideoDotNet.AdferoPhotos;

namespace Brafton.BlogEngine
{
    /// <summary>
    /// Imports articles from Brafton, ContentLEAD, and Castleford XML feeds.
    /// </summary>
    [Extension("Imports articles from Brafton, ContentLEAD, and Castleford XML feeds.", "0.5.dev", "<a href=\"http://contentlead.com/\">ContentLEAD</a>")]
    class BraftonArticleImporter
    {
        protected ExtensionSettings _settings;
        private static StreamWriter _logStream;
        private static object _logLock;

        /// <summary>
        /// Creates and attaches a new importer to the Post.Serving event.
        /// </summary>
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

            if (e.Location == ServingLocation.SinglePost)
                AddOpenGraphTags();
        }

        protected virtual void AddOpenGraphTags()
        {
            HttpContext context = HttpContext.Current;
            Guid postId = default(Guid);
            if (!Guid.TryParse(context.Request.QueryString["id"], out postId))
                return;

            if (context.CurrentHandler is System.Web.UI.Page)
            {
                System.Web.UI.Page page = (System.Web.UI.Page)context.CurrentHandler;
                Post post = Post.GetPost(postId);

                AppendOpenGraphPrefix(page, "og: http://ogp.me/ns#");
                AppendOpenGraphPrefix(page, "article: http://ogp.me/ns/article#");

                page.Header.Controls.Add(GenerateOpenGraphTag(OpenGraphTag.Type, "article"));
                page.Header.Controls.Add(GenerateOpenGraphTag(OpenGraphTag.Site_Name, BlogSettings.Instance.Name));
				page.Header.Controls.Add(GenerateOpenGraphTag(OpenGraphTag.Url, post.AbsoluteLink.ToString()));
                page.Header.Controls.Add(GenerateOpenGraphTag(OpenGraphTag.Title, post.Title));
                page.Header.Controls.Add(GenerateOpenGraphTag(OpenGraphTag.Article__Published_Time, post.DateCreated.ToString(@"yyyy-MM-ddTHH\:mm\:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture)));
				
				string desc = GetCleanPostDescription(post.Description);
				if (!string.IsNullOrEmpty(desc))
					page.Header.Controls.Add(GenerateOpenGraphTag(OpenGraphTag.Description, desc));
				
                string postImage = GetPostImageUrl(post);
                if (!string.IsNullOrEmpty(postImage))
                    page.Header.Controls.Add(GenerateOpenGraphTag(OpenGraphTag.Image, postImage));
            }
        }

        private void AppendOpenGraphPrefix(System.Web.UI.Page page, string content)
        {
            string prefix = page.Header.Attributes["prefix"] ?? "";
            page.Header.Attributes["prefix"] = (prefix + " " + content).Trim();
        }

        private string GetCleanPostDescription(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                return null;

            return Regex.Replace(desc, "<.*?>", string.Empty).Trim();
        }

        private string GetPostImageUrl(Post post)
        {
            Regex r = new Regex("<img.*?src=\"(.*?)\".*?/>");
            Match m = r.Match(post.Content);

            if (!m.Success)
                return null;

            return Utils.ConvertToAbsolute(m.Groups[1].ToString()).ToString();
        }

        protected System.Web.UI.Control GenerateOpenGraphTag(OpenGraphTag openGraphTag, string content)
        {
            StringBuilder sb = new StringBuilder();
            string tag = openGraphTag.ToString("g").Replace("__", ":").ToLower();

            sb.AppendFormat("<meta property=\"og:{0}\" content=\"{1}\" />", tag, content);
            sb.AppendLine();

            return new System.Web.UI.LiteralControl(sb.ToString());
        }

        protected void Import()
        {
            DateTime startTime = DateTime.Now;

            string importContent = _settings.GetSingleValue("ImportContent");
            switch (importContent)
            {
                case "Articles Only":
                    ImportArticles();
                    break;

                case "Videos Only":
                    ImportVideos();
                    break;

                case "Articles and Video":
                    ImportArticles();
                    ImportVideos();
                    break;

                default:
                    break;
            }

            Post.Reload();

            DateTime endTime = DateTime.Now;
            TimeSpan elapsed = endTime - startTime;

            Log(string.Format("Import finished; took {0}.", elapsed.ToString()), LogLevel.Debug);
        }

        private void ImportVideos()
        {
            string publicKey = _settings.GetSingleValue("VideoPublicKey");
            string secretKey = _settings.GetSingleValue("VideoSecretKey");
            int feedNumber = -1;

            if (!int.TryParse(_settings.GetSingleValue("VideoFeedNumber"), out feedNumber))
            {
                Log("Invalid video feed number. Stopping.", LogLevel.Error);
                return;
            }

            if (!ValidateVideoPublicKey(publicKey))
            {
                Log("Invalid video public key. Stopping.", LogLevel.Error);
                return;
            }

            if (!ValidateGuid(secretKey))
            {
                Log("Invalid video secret key. Stopping.", LogLevel.Error);
                return;
            }

            Log("Starting video import.", LogLevel.Debug);

            string baseUrl = "http://api.video.brafton.com/v2/";
            string basePhotoUrl = "http://pictures.directnews.co.uk/v2/";
            AdferoVideoClient videoClient = new AdferoVideoClient(baseUrl, publicKey, secretKey);
            AdferoClient client = new AdferoClient(baseUrl, publicKey, secretKey);
            AdferoPhotoClient photoClient = new AdferoPhotoClient(basePhotoUrl);

            AdferoVideoDotNet.AdferoArticles.ArticlePhotos.AdferoArticlePhotosClient photos = client.ArticlePhotos();
            string scaleAxis = AdferoVideoDotNet.AdferoPhotos.Photos.AdferoScaleAxis.X;

            AdferoVideoDotNet.AdferoArticles.Feeds.AdferoFeedsClient feeds = client.Feeds();
            AdferoVideoDotNet.AdferoArticles.Feeds.AdferoFeedList feedList = feeds.ListFeeds(0, 10);

            AdferoVideoDotNet.AdferoArticles.Articles.AdferoArticlesClient articles = client.Articles();
            AdferoVideoDotNet.AdferoArticles.Articles.AdferoArticleList articleList = articles.ListForFeed(feedList.Items[feedNumber].Id, "live", 0, 100);

            int articleCount = articleList.Items.Count;
            AdferoVideoDotNet.AdferoArticles.Categories.AdferoCategoriesClient categories = client.Categories();

            foreach (AdferoVideoDotNet.AdferoArticles.Articles.AdferoArticleListItem item in articleList.Items)
            {
                int brafId = item.Id;
                AdferoVideoDotNet.AdferoArticles.Articles.AdferoArticle article = articles.Get(brafId);

                Post p = FindPost(article);

                if (p == null)
                {
                    Log(string.Format("Importing new post '{0}' (ID {1}).", article.Fields["title"].Trim(), article.Id), LogLevel.Info);
                    p = ConvertToPost(article, categories, videoClient);

                    ImportCategories(p);

                    PhotoInstance? thumbnail = GetPhotoInstance(article, photos, photoClient, scaleAxis, 180);
                    PhotoInstance? fullSizePhoto = GetPhotoInstance(article, photos, photoClient, scaleAxis, 500);
                    ImportPhotos(p, thumbnail, fullSizePhoto);

                    if (!p.Valid)
                    {
                        Log(string.Format("Error: post '{0}' invalid: '{1}'", p.ValidationMessage), LogLevel.Error);
                        continue;
                    }
                    else
                        p.Save();
                }
            }
        }

        private void ImportArticles()
        {
            string apiKey = _settings.GetSingleValue("ApiKey");
            string baseUrl = _settings.GetSingleValue("BaseUrl");

            if (string.IsNullOrEmpty(apiKey))
            {
                Log("API key not set. Stopping.", LogLevel.Error);
                return;
            }

            if (!ValidateGuid(apiKey))
            {
                Log("Invalid API Key. Stopping.", LogLevel.Error);
                return;
            }

            Uri baseUri = new Uri(baseUrl);
            Uri feedUri = new Uri(baseUri, apiKey);

            Log(string.Format("Starting import at feed URI '{0}'.", feedUri.ToString()), LogLevel.Debug);
            ApiContext api = new ApiContext(apiKey, baseUrl);

            foreach (newsItem ni in api.News)
            {
                Post p = FindPost(ni);

                if (p == null)
                {
                    Log(string.Format("Importing new post '{0}' (ID {1}).", ni.headline.Trim(), ni.id), LogLevel.Info);
                    p = ConvertToPost(ni);

                    ImportCategories(p);

                    // it should noted that, technically, the xml can specify more than one photo.
                    PhotoInstance? thumbnail = GetPhotoInstance(ni, new enumeratedTypes.enumPhotoInstanceType[] { enumeratedTypes.enumPhotoInstanceType.Thumbnail, enumeratedTypes.enumPhotoInstanceType.Small });
                    PhotoInstance? fullSizePhoto = GetPhotoInstance(ni, enumeratedTypes.enumPhotoInstanceType.Large);

                    ImportPhotos(p, thumbnail, fullSizePhoto);

                    if (!p.Valid)
                    {
                        Log(string.Format("Error: post '{0}' invalid: '{1}'", p.ValidationMessage), LogLevel.Error);
                        continue;
                    }
                    else
                        p.Save();
                }
            }
        }

        private void ImportPhotos(Post p, PhotoInstance? thumbnail, PhotoInstance? fullSizePhoto)
        {
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

                if (fullSizePhoto != null && ImportPhoto(fullSizePhoto, physicalPicsPath, out fullSizePhysicalPath, out fullSizePhotoPath))
                {
                    photoImportCount++;
                    p.Content = AppendImageToContent(fullSizePhoto.Value, picsUri + fullSizePhotoPath, p.Content, "article-img-frame", "", true);
                }

                if (thumbnail != null && ImportPhoto(thumbnail, physicalPicsPath, out thumbnailPhysicalPath, out thumbnailPath))
                {
                    photoImportCount++;
                    p.Description = AppendImageToContent(thumbnail.Value, picsUri + thumbnailPath, p.Description, "article-thumbnail-frame");
                }

                if (photoImportCount > 0)
                    Log(string.Format("Imported {0} photo{1}.", photoImportCount, (photoImportCount != 1 ? "s" : "")), LogLevel.Info);
                else
                    Log("Warning: could not import any photos.", LogLevel.Warning);
            }
        }

        private void ImportCategories(Post p)
        {
            List<Category> added = new List<Category>();

            foreach (Category c in p.Categories)
            {
                Category ca = FindCategory(c);
                if (ca == null)
                {
                    if (c.Save() == SaveAction.Insert)
                    {
                        Log(string.Format("Imported new category '{0}'.", c.Title.Trim()), LogLevel.Info);
                        added.Add(c);
                    }
                }
                else
                    added.Add(ca);
            }

            p.Categories.Clear();
            p.Categories.AddRange(added);
        }
		
        protected string GetCleanCategoryName(string catName)
        {
            // blogengine treats categories with dashes as child categories.
            // HACK: this is an en dash.
            // not quite the same, but avoids ophaning this category.
            return catName.Replace("-", "–").Trim();
        }

        private Post FindPost(AdferoVideoDotNet.AdferoArticles.Articles.AdferoArticle a)
        {
            return Post.Posts.Find(p => p.Title.Trim() == a.Fields["title"].Trim());
        }

        private bool ValidateVideoPublicKey(string publicKey)
        {
            Regex reg = new Regex("[a-f0-9]{8}", RegexOptions.IgnoreCase);
            return reg.IsMatch(publicKey);
        }

        private bool ValidateGuid(string guid)
        {
            Regex reg = new Regex("[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", RegexOptions.IgnoreCase);
            return reg.IsMatch(guid);
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

        private bool ImportPhoto(PhotoInstance? photoInstance, string picsPath, out string physicalPhotoPath, out string photoPath)
        {
            string filename = photoInstance.Value.DestinationFileName;
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
                    photoPath = string.Empty;
                    return false;
                }
            }

            physicalPhotoPath = physicalPath;
            photoPath = filename;
            return true;
        }

        private Category FindCategory(string catName)
        {
            return FindCategory(new Category(catName, ""));
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

        private PhotoInstance? GetPhotoInstance(AdferoVideoDotNet.AdferoArticles.Articles.AdferoArticle article, AdferoVideoDotNet.AdferoArticles.ArticlePhotos.AdferoArticlePhotosClient photos, AdferoVideoDotNet.AdferoPhotos.AdferoPhotoClient photoClient, string scaleAxis, int scale)
        {
            PhotoInstance? inst = null;

            AdferoVideoDotNet.AdferoArticles.ArticlePhotos.AdferoArticlePhotoList photoList = photos.ListForArticle(article.Id, 0, 100);
            if (photoList.TotalCount > 0)
            {
                AdferoVideoDotNet.AdferoArticles.ArticlePhotos.AdferoArticlePhoto apho = photos.Get(photoList.Items[0].Id);
                int photoId = apho.SourcePhotoId;
                AdferoVideoDotNet.AdferoPhotos.Photos.AdferoPhoto pho = photoClient.Photos().GetScaleLocationUrl(photoId, scaleAxis, scale);
                string photoUrl = pho.LocationUri;
                string photoCaption = photos.Get(photoList.Items[0].Id).Fields["caption"];

                enumeratedTypes.enumPhotoOrientation ori = enumeratedTypes.enumPhotoOrientation.Landscape;
                if (scaleAxis == AdferoVideoDotNet.AdferoPhotos.Photos.AdferoScaleAxis.Y)
                    ori = enumeratedTypes.enumPhotoOrientation.Portrait;

                string cleanedUrl = photoUrl;
                if (cleanedUrl.IndexOf('?') >= 0)
                    cleanedUrl = cleanedUrl.Substring(0, cleanedUrl.IndexOf('?'));

                inst = new PhotoInstance()
                {
                    AltText = photoCaption,
                    Caption = photoCaption,
                    DestinationFileName = Slugify(article.Fields["title"]) + "-" + scale + Path.GetExtension(cleanedUrl),
                    Height = 0,
                    Id = apho.Id,
                    Orientation = ori,
                    Type = enumeratedTypes.enumPhotoInstanceType.Custom,
                    Url = photoUrl,
                    Width = 0
                };
            }

            return inst;
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

                        string cleanedUrl = phIns.Url;
                        if (cleanedUrl.IndexOf('?') >= 0)
                            cleanedUrl = cleanedUrl.Substring(0, cleanedUrl.IndexOf('?'));

                        string phTypeSlug = Slugify(phType);
                        phIns.DestinationFileName = Slugify(ni.headline) + (phTypeSlug == "" ? "" : "-" + phTypeSlug) + Path.GetExtension(cleanedUrl);

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

        private string Slugify(enumeratedTypes.enumPhotoInstanceType phType)
        {
            switch (phType)
            {
                case enumeratedTypes.enumPhotoInstanceType.Small:
                    return "small";
                case enumeratedTypes.enumPhotoInstanceType.Medium:
                    return "med";
                case enumeratedTypes.enumPhotoInstanceType.Thumbnail:
                    return "thumb";
                default:
                    return "";
            }
        }

        private Post ConvertToPost(AdferoVideoDotNet.AdferoArticles.Articles.AdferoArticle article, AdferoVideoDotNet.AdferoArticles.Categories.AdferoCategoriesClient categories, AdferoVideoClient videoClient)
        {
            Post p = new Post();

            p.Author = "Admin";
            AdferoVideoDotNet.AdferoArticles.Categories.AdferoCategoryList categoryList = categories.ListForArticle(article.Id, 0, 100);
            for (int i = 0; i < categoryList.TotalCount; i++)
            {
                AdferoVideoDotNet.AdferoArticles.Categories.AdferoCategory category = categories.Get(categoryList.Items[i].Id);
                p.Categories.Add(new Category(GetCleanCategoryName(category.Name), ""));
            }

            string embedCode = videoClient.VideoPlayers().GetWithFallback(article.Id, AdferoVideoDotNet.AdferoArticlesVideoExtensions.VideoPlayers.AdferoPlayers.RedBean, new AdferoVideoDotNet.AdferoArticlesVideoExtensions.VideoPlayers.AdferoVersion(1,0,0),AdferoVideoDotNet.AdferoArticlesVideoExtensions.VideoPlayers.AdferoPlayers.RcFlashPlayer, new AdferoVideoDotNet.AdferoArticlesVideoExtensions.VideoPlayers.AdferoVersion(1,0,0)).EmbedCode;
            p.Content = string.Format("<div class=\"videoContainer\">{0}</div> {1}", embedCode, article.Fields["content"]);

            p.DateCreated = DateTime.Parse(article.Fields["date"]);
            p.DateModified = DateTime.Parse(article.Fields["lastModifiedDate"]);
            p.Description = article.Fields["extract"];

            p.Slug = Slugify(article.Fields["title"]);
            p.Title = article.Fields["title"].Trim();

            return p;
        }

        private string Slugify(string input)
        {
            Regex alphaNumeric = new Regex("[^a-zA-Z0-9]+");
            return alphaNumeric.Replace(input.ToLower().Trim(), "-");
        }

        private Post ConvertToPost(newsItem ni)
        {
            Post p = new Post();

            p.Author = "Admin";
            foreach (category c in ni.categories)
                p.Categories.Add(new Category(GetCleanCategoryName(c.name), ""));
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

            p.Slug = Slugify(ni.headline);

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

            settings.AddParameter("ApiKey", "API Key", 36);

            settings.AddParameter("Interval", "Upload Interval (minutes)", 6, true);
            settings.AddValue("Interval", 180);

            settings.AddParameter("ImportedDate", "Imported Date");
            settings.SetParameterType("ImportedDate", ParameterType.DropDown);
            string[] sortOptions = { "Published Date", "Created Date", "Last Modified Date" };
            settings.AddValue("ImportedDate", sortOptions, sortOptions[0]);

            settings.AddParameter("LastUpload", "Time of last upload");
            settings.AddValue("LastUpload", DateTime.MinValue.ToString("u"));

            settings.AddParameter("ImportContent", "Import Content");
            settings.SetParameterType("ImportContent", ParameterType.DropDown);
            string[] contentTypes = { "Articles Only", "Videos Only", "Articles and Video" };
            settings.AddValue("ImportContent", contentTypes, contentTypes[0]);

            settings.AddParameter("VideoPublicKey", "Public Key");

            settings.AddParameter("VideoSecretKey", "Secret Key");

            settings.AddParameter("VideoFeedNumber", "Feed Number");
            settings.SetParameterType("VideoFeedNumber", ParameterType.Integer);

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

    enum OpenGraphTag
    {
        Title,
        Type,
        Url,
        Image,
        Description,
        Article__Published_Time,
        Site_Name
    }

    enum LogLevel
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
        public string DestinationFileName;
    }
}
