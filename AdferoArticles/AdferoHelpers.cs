using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml;
using System.IO;
using System.Net;

namespace AdferoVideoDotNet.AdferoArticles
{
    public class AdferoHelpers
    {
        public static string GetXmlFromUri(string uri)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(uri);

            return doc.OuterXml;
        }

        public static string GetRawResponse(string uri)
        {
            string result = string.Empty;
            WebRequest request = WebRequest.Create(uri);
            request.Timeout = 30 * 60 * 1000;
            request.UseDefaultCredentials = true;
            request.Proxy.Credentials = request.Credentials;
            WebResponse response = (WebResponse)request.GetResponse();

            using (StreamReader sr = new StreamReader(response.GetResponseStream()))
            {
                result = sr.ReadToEnd();
            }

            return result;
        }
    }
}