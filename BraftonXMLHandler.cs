using System;
using System.Xml;
using System.Xml.XPath;
using System.Globalization;
using System.Text;
using BlogEngine.Core.Providers;
using BlogEngine.Core.Web.Controls;
using BlogEngine.Core.Web.Extensions;
using BlogEngine.Core;
using System.IO;
using System.Net;

[Extension("BraftonXMLHandler", "1.1.0.0", "Brafton, Inc.")]
public class BraftonXMLHandler
{
    static protected ExtensionSettings _settings = null;

    /* Constructor
     * In order to accommodate the scheduling system, the extension subscribes to
     * the Post.Serving action.
     */
    public BraftonXMLHandler()
    {
		initSettings();
        Post.Serving += new EventHandler<ServingEventArgs>(ServingHandler);
    }

    /* Main function
     * The extension will run on a Wordpress-like scheduling system.  Serving a post
     * triggers a scheduling check, which determines if the interval has elapsed since
     * the previous XML load.  If it has, the process triggers, if not, it cuts out until
     * the next check. 
     */
    private void ServingHandler(object sender, ServingEventArgs e)
    {
        DateTime lastUpload = DateTime.Parse(_settings.GetSingleValue("LastUpload"));
        int interval = Int32.Parse(_settings.GetSingleValue("Interval"));
        if (DateTime.Now > lastUpload + TimeSpan.FromMinutes((double)interval))
        {
            loadXMLFeed();
            _settings.UpdateScalarValue("LastUpload", DateTime.Now.ToUniversalTime().ToString("u"));
        }
    }

    /* Settings declarations
     * The extension must store the URL of the feed, as well as timing information.  
     */
    private void initSettings()
    {
        ExtensionSettings settings = new ExtensionSettings(this);
        settings.IsScalar = true;
		
        settings.AddParameter("BaseUrl", "Feed Provider");
        string[] providers = { "http://api.brafton.com/", "http://api.contentlead.com/", "http://api.castleford.com.au/" };
        settings.AddValue("BaseUrl", providers, providers[0]);
		
        settings.AddParameter("ApiKey", "API Key", 36, true);
		
        settings.AddParameter("Interval", "Upload Interval (minutes)", 6, true);
        settings.AddValue("Interval", 180);
        //settings.IsRequiredParameter("Interval");
		
        /*
        settings.AddParameter("UseProxy", "Use Web Proxy", 10, true, false, ParameterType.Boolean);
        settings.AddValue("UseProxy", false);
                
        settings.AddParameter("ProxyURL", "Proxy URL (include port if applicable)");
        settings.AddValue("ProxyURL", "http://yourproxyaddress");
                
        settings.AddParameter("UseCredentials", "Use non-default credentials for proxy?", 
                                                        10, false, false, ParameterType.Boolean);
        settings.AddValue("UseCredentials", false);
                
        settings.AddParameter("ProxyUName", "Proxy Username");
        settings.AddValue("ProxyUName", "user");
                
        settings.AddParameter("ProxyPass", "Proxy Password");
        settings.AddValue("ProxyPass", "pass");*/
		
        settings.AddParameter("SortOrder", "Sort Order");
        settings.SetParameterType("SortOrder", ParameterType.DropDown);
        string[] sortOptions = { "Created Date", "Last Modified Date" };
        settings.AddValue("SortOrder", sortOptions, sortOptions[0]);
		
        settings.AddParameter("LastUpload", "Time of last upload");
        settings.AddValue("LastUpload", DateTime.MinValue.ToString("u"));
		
        _settings = ExtensionManager.InitSettings("BraftonXMLHandler", settings);
    }


    private XPathNavigator getUrls(String link, int num)
    {
        XPathNavigator xpath = null;
        WebRequest req = null;
        try
        {
            switch (num)
            {
                case 1:
                    req = (WebRequest)WebRequest.Create(_settings.GetSingleValue("BaseUrl") + _settings.GetSingleValue("ApiKey") + "/news");
                    break;
                case 2:
                    req = (WebRequest)WebRequest.Create(link);
                    break;
                case 3:
                    req = (WebRequest)WebRequest.Create(link + "/categories");
                    break;
                case 4:
                    req = (WebRequest)WebRequest.Create(link + "/photos");
                    break;
            }
			
            req.Method = "GET";
            //req.ContentType = "text/xml;charset=utf-8";
            req.Timeout = 600000;
			
            using (WebResponse res = (WebResponse)req.GetResponse())
            {
                Stream resStream = res.GetResponseStream();
                XPathDocument doc = new XPathDocument(resStream);
                xpath = doc.CreateNavigator();
            }
        }
        catch (Exception e)
        {
            FileStream file = new FileStream("C:\\inetpub\\wwwroot\\blogengine\\log.txt", FileMode.OpenOrCreate, FileAccess.Write);
            StreamWriter sw = new StreamWriter(file);
            sw.Write(e);
            sw.Close();
            file.Close();
            return null;   //could use better error reporting here
        }
        return xpath;

    }

    /* Feed Loading Logic
     * Procedures to parse the XML file, map it to BlogEngine Post fields, and save it to the
     * underlying database or XML structure.  
     */
    private void loadXMLFeed()
    {

        //initialize parser
        XPathNavigator xpath = getUrls("", 1);
        XPathNodeIterator articles = xpath.Select("//newsListItem");
		
        while (articles.MoveNext())
        {
            string[] paths = new string[4];
            String title = nodeValue("headline", articles.Current);
            String link = getAttribute("href", articles.Current);
			
            XPathNavigator xpathNews = getUrls(link, 2);
            XPathNodeIterator newsarticles = xpathNews.Select("/newsItem/text");
            String content = "";
            while (newsarticles.MoveNext())
            {
                content = newsarticles.Current.Value;
            }
			
			string extract = "";
			XPathNodeIterator extractp = xpathNews.Select("/newsItem/extract");
			while (extractp.MoveNext())
				extract = extractp.Current.Value;
			
            XPathNavigator xpathCat = getUrls(link, 3);
            Post post = new Post();
			
            Guid gid = post_exists(title);
            if (gid != Guid.Empty)
            {  //load old post
                post = Post.Load(gid);
            }
            else
            { //is new               
                post.Title = title;
				if (_settings.GetSingleValue("SortOrder") == "Created Date")
					post.DateCreated = formatDate(nodeValue("publishDate", articles.Current));
				else if (_settings.GetSingleValue("SortOrder") == "Last Modified Date")
					post.DateCreated = formatDate(nodeValue("lastModifiedDate", articles.Current));
				
                String slug = convertToURL(title);
                slug = String.Concat(slug, "-", nodeValue("id", articles.Current));
                post.Slug = slug;
				
                XPathNodeIterator tags = xpathNews.Select("/newsItem/keywords"); //add keywords as tags
                while (tags.MoveNext())
                {
                    String tagsin = tags.Current.Value;
                    String[] tagarray = tagsin.Split(',');
                    for (int i = 0; i < tagarray.Length; i++)
                        post.Tags.Add(tagarray[i]);
                }
                addCategories(post, xpathCat);
            }
			
            XPathNavigator xpathPic = getUrls(link, 4);
            XPathNodeIterator pic = xpathPic.Select("//photos/photo/instances/instance/url");
			
            string picture = " ";
            string[] picarray = new string[2]; //change to '2' for thumbnail
            int x = 0;
            while (pic.MoveNext())
            {
                if (x == 0) //change to '2' for thumbnail
                    picarray[x] = "<img class=\"article_pic\" src=\"" + pic.Current.Value + "\">  ";
				if (x == 1)
					picarray[x] = "<img class=\"article_pic\" src=\"" + pic.Current.Value + "\">  ";
                x++;
            }
			
            post.Content = picarray[0] + content; //change '0' to '1' for thumbnail
			if (!string.IsNullOrEmpty(extract))
				post.Description = picarray[1] + extract;
            post.Author = "Admin"; //set author name here
            post.Import();
        }
        Post.Reload();
    }

    /*
     * Find and return the value of a node within the given XPath object
     * The state of the XPathNavigator is unchanged.
     */
    private String nodeValue(String expression, XPathNavigator xpath)
    {
        XPathNodeIterator node = xpath.Select(expression);
        node.MoveNext();
        return node.Current.Value;
    }

    /*
     * Find and return an attribute value within the given node. Returns
     * the node to its previous state after storing this value.
     */
    private String getAttribute(String attribute, XPathNavigator xpath)
    {
        xpath.MoveToAttribute(attribute, "");
        string val = xpath.Value;
        xpath.MoveToParent();
        return val;
    }

    /*
     * Check through the listing of posts for this one.  Headings provide the 
     * most unique means of identification.
     */
    private Guid post_exists(String heading)
    {
        foreach (Post post in Post.Posts)
        {
            if (post.Title.Equals(heading))
            {
                return post.Id;
            }
        }
        return Guid.Empty;
    }

    /*
     * Translates the <Date> and <Created> BraftonXML fields into a DateTime object
     */
    private DateTime formatDate(String date)
    {
        String[] date_split = date.Split('-', 'T', ':');
        int year = Int32.Parse(date_split[0]);
        int month = Int32.Parse(date_split[1]);
        int day = Int32.Parse(date_split[2]);
        int hour = Int32.Parse(date_split[3]);
        int minute = Int32.Parse(date_split[4]);
        int second = Int32.Parse(date_split[5]);
        return new DateTime(year, month, day, hour, minute, second);
    }

    /*
     * Url conversion method strips punctuation, spaces words 
     * with hyphens, and limits the length of the string. 
     * Written by Jeff Atwood, posted on StackOverflow
     */
    private String convertToURL(String title)
    {
        if (String.IsNullOrEmpty(title)) return "";
		
        // to lowercase, trim extra spaces
        title = title.ToLower().Trim();
		
        int len = title.Length;
        StringBuilder sb = new StringBuilder(len);
        bool prevdash = false;
        char c;
		
        //replace punctuation
        for (int i = 0; i < title.Length; i++)
        {
            c = title[i];
            if (c == ' ' || c == ',' || c == '.' || c == '/' || c == '\\' || c == '-')
            {
                if (!prevdash)
                {
                    sb.Append('-');
                    prevdash = true;
                }
            }
            else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
                prevdash = false;
            }
            if (i == 60) break;
        }
		
        title = sb.ToString();
        // remove trailing dash, if there is one
        if (title.EndsWith("-"))
            title = title.Substring(0, title.Length - 1);
        return title;
    }

    /*
     * Iterates through all <Category> items assigned to a feed.
     * Initializes and saves any new categories, then assigns
     * them to the current post.
     */
    private void addCategories(Post post, XPathNavigator xpath)
    {
        //XPathNodeIterator result = xpath.Select("Categories/Category");
        XPathNodeIterator result = xpath.Select("//categories/category/name");
        while (result.MoveNext())
        {
            Category newcat = new Category(result.Current.Value, "");
            bool toAdd = true;
            //search for existing instances of category
            foreach (Category cat in Category.Categories)
            {
                if (cat.CompareTo(newcat) == 0)
                {
                    newcat = cat;
                    toAdd = false;
                    break;
                }
            }
			
            //if category is not found in existing list, add to database
            if (toAdd)
            {
                BlogService.InsertCategory(newcat);
            }
			
            //add to post                        
            post.Categories.Add(newcat);
        }
    }

    private void outputToLog(Stream st)
    {
        FileStream file = new FileStream("C:\\inetpub\\wwwroot\\blogengine\\log.txt", FileMode.OpenOrCreate, FileAccess.Write);
        StreamWriter sw = new StreamWriter(file);
        StreamReader reader = new StreamReader(st);
        sw.Write(reader.ReadToEnd());
        reader.Close();
        sw.Close();
        file.Close();
    }
}