﻿using System;
using System.Collections.Generic;
using System.Web;
using System.Xml;

namespace AdferoVideoDotNet.AdferoArticles.Articles
{
    /// <summary>
    /// Client that provides article related functions.
    /// </summary>
    public class AdferoArticlesClient
    {
        private string baseUri;
        private AdferoCredentials credentials;

        /// <summary>
        /// Initialises a new instance of the Articles Client
        /// </summary>
        /// <param name="baseUri">Uri of the API provided by your account manager</param>
        /// <param name="credentials">Credentials object containing public key and secret key</param>
        public AdferoArticlesClient(string baseUri, AdferoCredentials credentials)
        {
            this.baseUri = baseUri;
            this.credentials = credentials;
        }

        private AdferoArticle GetArticleFromXmlString(string xml)
        {
            AdferoArticle article = new AdferoArticle();
            Dictionary<string, string> fields = new Dictionary<string, string>();
            XmlDocument doc = new XmlDocument();
            doc.Load(xml);

            XmlNodeList children = doc.SelectNodes("/article/node()");

            foreach (XmlNode n in children)
            {
                switch (n.Name)
                {
                    case "id":
                        article.Id = int.Parse(n.Value);
                        break;

                    case "briefId":
                        article.FeedId = int.Parse(n.Value);
                        break;

                    case "state":
                        article.State = n.Value;
                        break;

                    case "fields":
                        foreach (XmlNode f in n.ChildNodes)
                            article.Fields.Add(f.Name, f.Value);
                        break;

                    default:
                        break;
                }
            }

            return article;
        }

        private string GetUri(int id, string identifier, string format, string[] properties, string[] fields, int? offset, int? limit)
        {
            Dictionary<string, string> data = new Dictionary<string, string>();

            if (properties != null && properties.Length > 0)
                data["properties"] = string.Join(",", properties);

            if (fields != null && fields.Length > 0)
                data["fields"] = string.Join(",", fields);

            if (!string.IsNullOrEmpty(identifier) || offset != null || limit != null)
            {
                data["offset"] = offset.ToString();
                data["limit"] = limit.ToString();
                data[identifier] = id.ToString();

                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, string> kv in data)
                    parts.Add(string.Format("{0}={1}", kv.Key, kv.Value));
                string queryString = HttpUtility.UrlDecode(string.Join("&", parts.ToArray()));

                return string.Format("{0}articles.{1}?{2}", this.baseUri, format, queryString);
            }
            else
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, string> kv in data)
                    parts.Add(string.Format("{0}={1}", kv.Key, kv.Value));
                string queryString = HttpUtility.UrlDecode(string.Join("&", parts.ToArray()));

                return string.Format("{0}articles/{1}.{2}?{3}", this.baseUri, id, format, queryString);
            }
        }

        private AdferoArticle GetArticle(int id, string[] properties, string[] fields)
        {
            string uri = this.GetUri(id, null, "xml", properties, fields, null, null);
            uri = string.Format("http://{0}:{1}@{2}", this.credentials.PublicKey, this.credentials.SecretKey, uri.Replace("http://", string.Empty));
            string xml = AdferoHelpers.GetXmlFromUri(uri);

            return this.GetArticleFromXmlString(xml);
        }

        public AdferoArticle Get(int id)
        {
            return this.GetArticle(id, null, null);
        }

        private string GetArticleRaw(int id, string[] properties, string[] fields, string format)
        {
            string uri = string.Empty;

            switch (format)
            {
                case "xml":
                    uri = this.GetUri(id, null, "xml", properties, fields, null, null);
                    break;

                case "json":
                    uri = this.GetUri(id, null, "json", properties, fields, null, null);
                    break;

                default:
                    throw new ArgumentException(string.Format("{0} format not supported", format));
            }

            uri = string.Format("http://{0}:{1}@{2}", this.credentials.PublicKey, this.credentials.SecretKey, uri.Replace("http://", string.Empty));
            return AdferoHelpers.GetRawResponse(uri);
        }

        public string GetRaw(int id, string format)
        {
            if (string.IsNullOrEmpty(format))
                throw new ArgumentException("format is required");

            return this.GetArticleRaw(id, null, null, format);
        }

        private AdferoArticleList ListArticlesFromXmlString(string xml)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(xml);
            int totalCount = int.Parse(doc.SelectSingleNode("/articles").Attributes["totalCount"].Value);
            AdferoArticleList articleList = new AdferoArticleList();
            articleList.TotalCount = totalCount;

            foreach (XmlNode n in doc.SelectNodes("/articles/article"))
            {
                foreach (XmlNode na in n.SelectNodes("/id"))
                {
                    AdferoArticleListItem article = new AdferoArticleListItem();
                    article.Id = int.Parse(na.Value);
                    articleList.Items.Add(article);
                }
            }

            return articleList;
        }

        private AdferoArticleList ListArticlesForBrief(int briefId, int offset, int limit, string state, string[] properties, string[] fields)
        {
            string uri = this.GetUri(briefId, "briefId", "xml", properties, fields, offset, limit);
            uri = string.Format("http://{0}:{1}@{2}", this.credentials.PublicKey, this.credentials.SecretKey, uri.Replace("http://", string.Empty));
            string xml = AdferoHelpers.GetXmlFromUri(string.Format("{0}&state={1}", uri, state));

            AdferoArticleList articles = this.ListArticlesFromXmlString(xml);
            articles.Limit = limit;
            articles.Offset = offset;

            return articles;
        }

        public AdferoArticleList ListForBrief(int briefId, string state, int offset, int limit)
        {
            if (string.IsNullOrEmpty(state))
                throw new ArgumentException("state is required");

            return this.ListArticlesForBrief(briefId, offset, limit, state, null, null);
        }

        private string ListArticlesForBriefRaw(int briefId, int offset, int limit, string state, string[] properties, string[] fields, string format)
        {
            string uri = string.Empty;
            switch (format)
            {
                case "xml":
                    uri = this.GetUri(briefId, "briefId", "xml", properties, fields, offset, limit);
                    break;

                case "json":
                    uri = this.GetUri(briefId, "briefId", "json", properties, fields, offset, limit);
                    break;

                default:
                    throw new ArgumentException(string.Format("{0} format not supported", format));
            }

            uri = string.Format("http://{0}:{1}@{2}", this.credentials.PublicKey, this.credentials.SecretKey, uri.Replace("http://", string.Empty));
            return AdferoHelpers.GetRawResponse(string.Format("{0}&state={1}", uri, state));
        }

        private AdferoArticleList ListArticlesForFeed(int feedId, int offset, int limit, string state, string[] properties, string[] fields)
        {
            string uri = this.GetUri(feedId, "feedId", "xml", properties, fields, offset, limit);
            uri = string.Format("http://{0}:{1}@{2}", this.credentials.PublicKey, this.credentials.SecretKey, uri.Replace("http://", string.Empty));
            string xml = AdferoHelpers.GetXmlFromUri(string.Format("{0}&state={1}", uri, state));

            AdferoArticleList articles = this.ListArticlesFromXmlString(xml);
            articles.Limit = limit;
            articles.Offset = offset;

            return articles;
        }

        private string ListArticlesForFeedRaw(int feedId, int offset, int limit, string state, string[] properties, string[] fields, string format)
        {
            string uri = string.Empty;
            switch (format)
            {
                case "xml":
                    uri = this.GetUri(feedId, "feedId", "xml", properties, fields, offset, limit);
                    break;

                case "json":
                    uri = this.GetUri(feedId, "feedId", "json", properties, fields, offset, limit);
                    break;

                default:
                    throw new ArgumentException(string.Format("{0} format not supported", format));
            }

            uri = string.Format("http://{0}:{1}@{2}", this.credentials.PublicKey, this.credentials.SecretKey, uri.Replace("http://", string.Empty));
            return AdferoHelpers.GetRawResponse(string.Format("{0}&state={1}", uri, state));
        }
    }
}