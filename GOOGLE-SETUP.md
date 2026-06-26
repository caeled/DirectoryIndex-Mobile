# Google Cloud Setup (required before building)

## 1. Create a Google Cloud project

1. Go to https://console.cloud.google.com
2. Click "New Project", give it a name like "DriveIndex", click Create
3. Make sure it's selected in the top dropdown

## 2. Enable the Google Drive API

1. Go to APIs & Services > Library
2. Search for "Google Drive API"
3. Click it and click Enable

## 3. Create OAuth credentials

1. Go to APIs & Services > Credentials
2. Click "Create Credentials" > "OAuth client ID"
3. If prompted, configure the OAuth consent screen first:
   - User Type: External
   - App name: Drive Index
   - Support email: your email
   - Save and continue through the rest
4. Back to Create Credentials > OAuth client ID
5. Application type: Desktop app
6. Name: Drive Index
7. Click Create
8. Copy the Client ID and Client Secret shown

## 4. Add credentials to the app

Open:  DriveIndexApp/Services/GoogleDriveService.cs

Replace these two lines:
    private const string ClientId     = "YOUR_CLIENT_ID";
    private const string ClientSecret = "YOUR_CLIENT_SECRET";

With your actual values from step 8.

## 5. Add test users (while app is in testing)

1. Go to APIs & Services > OAuth consent screen
2. Scroll to "Test users"
3. Add your Gmail address
4. Save

You can now build and test the app.
