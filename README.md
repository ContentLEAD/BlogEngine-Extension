Brafton Article Importer for BlogEngine
==
The Brafton Article Importer loads custom content from Brafton, ContentLEAD, and Castleford XML feeds.

## Prerequisites ##
1. .NET Framework 3.5 or higher
2. BlogEngine 2.5+ (2.7 recommended)

## Installation ##
1. If you have an old version of the extension installed, *you must delete it*. Your settings will be saved.
2. Extract the contents of the zip file into the **App_Code/Extensions folder**.

## Configuration ##
In the BlogEngine Admin section, browse to **Appearance** > **BraftonArticleImporter**. Here you will find settings to configure the importer, as described below.

These settings are for the version 0.4; if you have a lower version please update before referencing this document.

### Standard Settings ###
These settings are **required** for the importer to work properly.

- **Feed Provider**: The domain from where your articles are served from. This will match the information given to you by your account manager.
- **API Key**: Your unique access key. This will match the information given to you by your account manager.
- **Upload Interval**: How often the importer will check for new posts, in minutes. *It is highly recommended* that you leave it at the default value of **180**. Lower values can cause excess strain on your server.

### Advanced Settings ###
These settings are **optional**, and care should be taken when undergoing modifications.

- **Imported Date**: Sets the "created date" of the imported article *within the BlogEngine installation*. This can be one of the following:
 - ***Created Date***: When the article was started by the writer
 - ***Published Date***: When the article was approved by the you, the client
 - ***Last Modified Date***: When the article was last edited by the writer
- **Time of Last Upload**: Marks the last time the importer was run, successfully or otherwise. *Please do not make changes to this date.*
- **Import Content**: Content types that will be imported. This should be set to **Articles Only** unless you have signed up for a video subscription.
- **Public Key**: Your public key for video subscriptions. This should be blank unless you have signed up for a video subscription.
- **Secret Key**: Your unique, private key for video subscriptions. This should be blank unless you have signed up for a video subscription.
- **Feed Number**: The feed number for video subscriptions. This should be blank unless you have signed up for a video subscription.

## Debugging ##
The importer stores a detailed log of its state and actions within **App_Data/BraftonArticleImporter.log**. You are free to delete, rename, or move this file, as the importer will create a new one in its place.

When contacting support, *please include this file* to facilitate a quick response.