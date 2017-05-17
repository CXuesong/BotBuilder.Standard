# Notes on the Portable Version of Microsoft Bot Builder

>   For Half Moon. Though I might have wandered much too far from the Tribe or the Clans.

I wanted to run my bot on Linux with C#, so I need ASP.Net Core. After taking a look at Microsoft/BotBuilder#572 and Microsoft/BotBuilder#2289, I decided to migrate the library to .NET Standard, and at least, get it working in certain situations. This ported library set is maintained by an individual and is not official, thus I DO NOT SUGGEST YOU TO USE IT IN PRODUCTION ENVIRONMENT. Still, well, PR is welcomed ​:joy_cat:​

In order to use the built library, I have uploaded it to NuGet. You may install its parts by running the following commands in the [Package Manager Console](https://docs.nuget.org/docs/start-here/using-the-package-manager-console)

​	To install `Microsoft.Bot.Connector` for ASP.NET Core

```powershell
Install-Package CXuesong.Ported.Microsoft.Bot.Connector.AspNetCore -Pre
```

​	To install `Microsoft.Bot.Builder`

```powershell
Install-Package CXuesong.Ported.Microsoft.Bot.Builder -Pre
Install-Package CXuesong.Ported.Microsoft.Bot.Builder.Autofac -Pre
```

​	To install `Microsoft.Bot.Builder.FormFlow.Json`

```powershell
Install-Package CXuesong.Ported.Microsoft.Bot.Builder.FormFlow.Json -Pre
```

This folder contains ported .Net Standard 1.6 version of Microsoft Bot Builder SDK. This ported version is not complete yet. For now, it contains

-   Microsoft.Bot.Connector.Standard
-   Microsoft.Bot.Connector.AspNetCore
-   Microsoft.Bot.Builder.Standard
-   Microsoft.Bot.Builder.Autofac.Standard
-   Microsoft.Bot.Builder.FormFlow.Json.Standard

I also ported some working examples to .NET Core. including

-   Microsoft.Bot.Sample.AspNetCore.SimpleEchoBot
-   Microsoft.Bot.Sample.AspNetCore.EchoBot
-   Microsoft.Bot.Sample.AspNetCore.AlarmBot
-   Microsoft.Bot.Sample.AspNetCore.PizzaBot
-   Microsoft.Bot.Sample.AspNetCore.AnnotatedSandwichBot

Since I haven't ported any of the unit tests, there might still exist bugs in the library. And it only supports .NET Core. Migration of Microsoft.Bot.Connector(.NetFramework) is needed before this ported library can be consumed on .Net Framework.

I set up the ASP.Net Core projects in VS 2017, which means that you may need VS 2017 to properly open the whole solution. The library targets at .Net Standard 1.6 (though some parts target at 1.4), so if this project were to be made compatible with both .Net Core and .Net Framework, you would at least need .Net Framework 4.6 to consume this library.

## Implementation Details

Because the migration from .Net Framework to .Net Standard may lead to some big changes, so I decided to use different subfolder names and keep the unported version intact. However, these assemblies share the same names with the unported ones.

For detailed usages, please take a look at the example projects, starting with `Microsoft.Bot.Sample.AspNetCore.SimpleEchoBot`.

### Microsoft.Bot.Connector

I copied the source code from `Microsoft.Bot.Connector.Shared` to `Microsoft.Bot.Connector.Standard`. `NET45` conditional switch is disabled. I did't remove these code.

The reference in `Microsoft.Bot.Connector.NetCore` has been changed from the Shared project to Standard project.

Some adjustments have been made to `TrustServiceUrlAttribute`, so that it can properly handle the case when you leave `MicrosoftAppId` empty. It used to throw `OAuthException` in this case.

Note that you need to register a singleton of `MicrosoftAppCredentials` in `ConfigureServices`, and call `options.Filters.Add(new TrustServiceUrlAttribute())` in `service.AddMvc` call. (due to the adjustments mentioned above, here we use `new` instead of `typeof`)

If you come across issue with the version of `System.Http`, installing a newer version of `System.Http` from NuGet will usually solves it.

### Microsoft.Bot.Builder & Microsoft.Bot.Builder.Autofac

This is where the hard work lies. I had to repetitively try out the example projects, find the bugs, and fix them. Some bugs may not be so obvious, and I can not guarantee that I've fixed them all. If you find new bugs, please leave me a note or open a PR. Thank you!

#### Things other than serialization

I have stripped `static` qualifier from `Microsoft.Bot.Builder.Dialogs.Conversation`, simply because it needs credential information, which cannot be loaded via `MicrosoftAppCredentials`'s parameterless constructor in .Net Core. Now `Conversation` should be registered as an singleton in `ConfigureServices`. You may take a look at `Microsoft.Bot.Sample.AspNetCore.EchoBot/Startup.cs`. When you need  `Conversation`, just use DI.

I have ported Chronic to .Net Standard. The package is `CXuesong.Ported.Chronic`.

#### Things about serialization

First of all, it's well known that this library heavily relies on `BinaryFormatter`, which is unfortunately not available before .Net Standard 2.0. There is even no `[Serializable]` here.

To resolve this dependency, I attempted to use some DataContractSerializer instead; however, such serializers often require all the `Type`s of the serialized instances known before serialization. Finally, I chose to use `Newtonsoft.Json` with some customizations.

I replaced `FormatterStore` with `DataContractStore`, implemented some `JsonConverter`s in Fibers/JsonConverters.cs, and removed Fibers/Serialization.cs. Here are some key points

*   Object references are kept by default, except the arrays and lists. Cyclic references are retained.
*   It persists the type information of every instances, so don't worry about inheritance.
*   It can handle the serialization of `Delegate`, and `Regex`. I also wrote `ResolvableObjectJsonConverter` that, I believe, is equivalent to `StoreInstanceByTypeSurrogate`.
*   For you classes that need to be stored, or to be "marked as serializable" as mentioned in official document, I suggest you do the following
    1.  Apply `[DataContract]` attribute to your class
    2.  Since DataContract is opt-in, you need to apply `[DataMember]` attribute to your fields and properties that need to be serialized.
    3.  If necessary, you can write serialization callbacks and apply them with `[OnSerializing]`, `[OnSerialized]`, `[OnDesrializing]`, or `[OnDeserialized]`.
    4.  You can dive into the documentation of `Newtonsoft.Json` for more tips on serialization.
*   The above procedure is only my routine, because I was to use DataContract at first. You may well use `[JsonObject]`, `[JsonProperty]`, `[JsonConstructor]`, if necessary.

Since I used `JsonSerializer` to handle the serialization process, there's some caveats

-   You need to be careful when declaring a field of `object` type. In some cases, the deserialized instance may not be what you want (e.g. just a `JToken` object). For now it's known that you cannot put a delegate into such field, as they will be deserialized to some other internal structure instead of the delegate itself.
-   You should be extremely careful when cyclic references present. It's recommended that at least you provide a private parameterless constructor for your serializable classes.
    -   Because rather than create a class instance without invoking its constructor, `JsonSerializer` instantiates a class, then fills the members.
    -   If you are familiar with `JsonSerializer`, you may know that a class can also be instantiated and populated through its constructor, but still please provide a non-public parameterless constructor, especially when you know your class may be in the middle of some reference loops.
    -   Let's consider the following case: we have a reference loop A→B→A. If there is no parameterless constructor in A, `JsonSerializer` will try to instantiate B first, then pass B into A's constructor. However, even when B has been completely constructed, serialized still have no instance A in its reference dictionary, and B→A will be broken (the property in B will leave as its default value).
    -   I have written a StrictJsonReferenceResolver for debugging purpose, which will throw an Exception if there exists a reference that cannot be restored. Note that it's only for debugging. At least, it's not thread-safe.To enable it, just enable `STRICT_REF_RESOLVER` conditional switch in `Store.cs`.


This is all I can retrospect for now. Anyway, I hope you will be able to enjoy the Bot Framework on .Net Core `*^_^*`

CXuesong a.k.a. forest93
