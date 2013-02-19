using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using AdferoVideoDotNet.AdferoArticlesVideoExtensions.VideoOutputs;
using System.Text.RegularExpressions;
using AdferoVideoDotNet.AdferoArticlesVideoExtensions.VideoPlayers;

namespace AdferoVideoDotNet.AdferoArticlesVideoExtensions
{
    public class AdferoVideoClient : AdferoArticles.AdferoClient
    {
        public AdferoVideoClient(string baseUri, string publicKey, string secretKey)
        {
            Regex reg = new Regex("^http://[a-z0-9-]+(.[a-z0-9-]+)*(:[0-9]+)?(/.*)?$", RegexOptions.IgnoreCase);
            if (!reg.IsMatch(baseUri))
                throw new ArgumentException("Not a valid uri");

            if (!baseUri.EndsWith("/"))
                baseUri += "/";

            this.baseUri = baseUri;
            this.credentials = new AdferoArticles.AdferoCredentials(publicKey, secretKey);
        }

        public AdferoVideoOutputsClient VideoOutputs()
        {
            return new AdferoVideoOutputsClient(this.baseUri, this.credentials);
        }

        public AdferoVideoPlayersClient VideoPlayers()
        {
            return new AdferoVideoPlayersClient(this.baseUri, this.credentials);
        }
    }
}