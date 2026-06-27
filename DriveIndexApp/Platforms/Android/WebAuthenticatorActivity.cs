using Android.App;
using Android.Content;
using Android.Content.PM;

namespace DriveIndexApp;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "com.googleusercontent.apps.199712399872-u0huta0qh4kinlamm5q8rpo39lh2enqe",
    DataPath = "/oauth2redirect")]
public class WebAuthenticatorActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity { }
