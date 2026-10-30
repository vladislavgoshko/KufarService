namespace KufarBot.Core.Analytics;

/// <summary>eph-17: canonical event type values stored in UsageEvents.EventType,
/// plus the property keys used inside UsageEvents.PropertiesJson.</summary>
public static class UsageEventTypes
{
    public const string BotStarted = "bot_started";
    public const string SearchCreated = "search_created";
    public const string SearchUpdated = "search_updated";
    public const string SearchDeleted = "search_deleted";
    public const string PresetSelected = "preset_selected";
    public const string ListingNotificationSent = "listing_notification_sent";
    public const string ListingLinkClicked = "listing_link_clicked";
    public const string LimitReached = "limit_reached";
    public const string FeatureUsed = "feature_used";

    // eph-18: ready-search funnel — explicit refusal to create from a preset/card.
    public const string PresetDeclined = "preset_declined";

    // Property keys in PropertiesJson.
    public const string PropFeature = "feature";
    public const string PropPreset = "preset";
    public const string PropTarget = "target";
}

/// <summary>Well-known values for the "feature" property of FeatureUsed events.</summary>
public static class UsageFeatures
{
    public const string Preview = "preview";
    public const string Favorite = "favorite";
    public const string OwnQuery = "own_query";
    public const string OwnKeywordsInclude = "own_kw_inc";
    public const string OwnKeywordsExclude = "own_kw_exc";
    public const string OwnSellerType = "own_seller";
    public const string OwnMaxAge = "own_age";

    // eph-18: ready-made search presets and user templates.
    public const string PresetCatalogView = "preset_catalog_view";
    public const string PresetCreated = "preset_created";
    public const string TemplateSaved = "template_saved";
    public const string TemplateCreated = "template_created";
    public const string TemplateRenamed = "template_renamed";
    public const string TemplateDeleted = "template_deleted";
}
