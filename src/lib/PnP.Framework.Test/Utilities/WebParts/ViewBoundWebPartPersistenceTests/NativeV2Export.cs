// Exact captured DWP text, not a recomposed or normalized Web Part.
// Source: CCD-145 attachment 5d44c42d-bd72-41bc-ac2d-12799f562162, raw export blob.
// Page: d4fda766-a38d-4122-98cd-ddd3406d5a0a; version: {D4FDA766-A38D-4122-98CD-DDD3406D5A0A},595.
namespace PnP.Framework.Test.Utilities.WebParts
{
    internal static class NativeV2Export
    {
        internal const string Sha256 = "d0aeba4597352cc7df87c9fbd1b58f45fe17ff20f50b4f4c3e121cee431b5ea7";

        internal const string Xml =
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" +
            "<WebPart xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"http://schemas.microsoft.com/WebPart/v2\">\r\n" +
            "  <Title>App Publishing Calendar</Title>\r\n" +
            "  <FrameType>None</FrameType>\r\n" +
            "  <Description />\r\n" +
            "  <IsIncluded>true</IsIncluded>\r\n" +
            "  <ZoneID>wpz</ZoneID>\r\n" +
            "  <PartOrder>0</PartOrder>\r\n" +
            "  <FrameState>Normal</FrameState>\r\n" +
            "  <Height />\r\n" +
            "  <Width />\r\n" +
            "  <AllowRemove>true</AllowRemove>\r\n" +
            "  <AllowZoneChange>true</AllowZoneChange>\r\n" +
            "  <AllowMinimize>true</AllowMinimize>\r\n" +
            "  <AllowConnect>true</AllowConnect>\r\n" +
            "  <AllowEdit>true</AllowEdit>\r\n" +
            "  <AllowHide>true</AllowHide>\r\n" +
            "  <IsVisible>true</IsVisible>\r\n" +
            "  <DetailLink>/sites/DevCenter/Learn/Lists/Events Calendar</DetailLink>\r\n" +
            "  <HelpLink />\r\n" +
            "  <HelpMode>Modeless</HelpMode>\r\n" +
            "  <Dir>Default</Dir>\r\n" +
            "  <PartImageSmall />\r\n" +
            "  <MissingAssembly>Cannot import this Web Part.</MissingAssembly>\r\n" +
            "  <PartImageLarge>/_layouts/15/images/itevent.png?rev=37</PartImageLarge>\r\n" +
            "  <IsIncludedFilter />\r\n" +
            "  <Assembly>Microsoft.SharePoint.Core, Version=16.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c</Assembly>\r\n" +
            "  <TypeName>Microsoft.SharePoint.WebPartPages.ListViewWebPart</TypeName>\r\n" +
            "  <WebId xmlns=\"http://schemas.microsoft.com/WebPart/v2/ListView\">00000000-0000-0000-0000-000000000000</WebId>\r\n" +
            "  <ListViewXml xmlns=\"http://schemas.microsoft.com/WebPart/v2/ListView\">&lt;View Name=\"{CB9ADC96-A41F-478F-B48D-E10D845D3752}\" MobileView=\"TRUE\" Hidden=\"TRUE\" RecurrenceRowset=\"TRUE\" Type=\"CALENDAR\" TabularView=\"FALSE\" DisplayName=\"\" Url=\"/sites/DevCenter/Learn/Pages/App-publishing-calendar.aspx\" Level=\"1\" BaseViewID=\"2\" ContentTypeID=\"0x\" MobileUrl=\"_layouts/15/mobile/viewdaily.aspx\" ImageUrl=\"/_layouts/15/images/events.png?rev=50\"&gt;&lt;Toolbar Type=\"None\" /&gt;&lt;ViewHeader /&gt;&lt;ViewBody /&gt;&lt;ViewFooter /&gt;&lt;ViewEmpty /&gt;&lt;ParameterBindings&gt;&lt;ParameterBinding Name=\"NoAnnouncements\" Location=\"Resource(wss,noXinviewofY_LIST)\" /&gt;&lt;ParameterBinding Name=\"NoAnnouncementsHowTo\" Location=\"Resource(wss,noXinviewofY_DEFAULT)\" /&gt;&lt;/ParameterBindings&gt;&lt;ViewFields&gt;&lt;FieldRef Name=\"EventDate\" /&gt;&lt;FieldRef Name=\"EndDate\" /&gt;&lt;FieldRef Name=\"fRecurrence\" /&gt;&lt;FieldRef Name=\"EventType\" /&gt;&lt;FieldRef Name=\"WorkspaceLink\" /&gt;&lt;FieldRef Name=\"Title\" /&gt;&lt;FieldRef Name=\"Location\" /&gt;&lt;FieldRef Name=\"Description\" /&gt;&lt;FieldRef Name=\"Workspace\" /&gt;&lt;FieldRef Name=\"MasterSeriesItemID\" /&gt;&lt;FieldRef Name=\"fAllDayEvent\" /&gt;&lt;/ViewFields&gt;&lt;ViewData&gt;&lt;FieldRef Name=\"Title\" Type=\"CalendarMonthTitle\" /&gt;&lt;FieldRef Name=\"Title\" Type=\"CalendarWeekTitle\" /&gt;&lt;FieldRef Name=\"Location\" Type=\"CalendarWeekLocation\" /&gt;&lt;FieldRef Name=\"Title\" Type=\"CalendarDayTitle\" /&gt;&lt;FieldRef Name=\"Location\" Type=\"CalendarDayLocation\" /&gt;&lt;/ViewData&gt;&lt;Query&gt;&lt;Where&gt;&lt;DateRangesOverlap&gt;&lt;FieldRef Name=\"EventDate\" /&gt;&lt;FieldRef Name=\"EndDate\" /&gt;&lt;FieldRef Name=\"RecurrenceID\" /&gt;&lt;Value Type=\"DateTime\"&gt;&lt;Month /&gt;&lt;/Value&gt;&lt;/DateRangesOverlap&gt;&lt;/Where&gt;&lt;/Query&gt;&lt;/View&gt;</ListViewXml>\r\n" +
            "  <ListName xmlns=\"http://schemas.microsoft.com/WebPart/v2/ListView\">{EDD50B31-96C8-4AB0-B607-45BDCB34F9B5}</ListName>\r\n" +
            "  <ListId xmlns=\"http://schemas.microsoft.com/WebPart/v2/ListView\">edd50b31-96c8-4ab0-b607-45bdcb34f9b5</ListId>\r\n" +
            "  <ViewFlag xmlns=\"http://schemas.microsoft.com/WebPart/v2/ListView\">8921097</ViewFlag>\r\n" +
            "  <ViewFlags xmlns=\"http://schemas.microsoft.com/WebPart/v2/ListView\">Html Hidden RecurrenceRowset Calendar Mobile</ViewFlags>\r\n" +
            "  <ViewContentTypeId xmlns=\"http://schemas.microsoft.com/WebPart/v2/ListView\">0x</ViewContentTypeId>\r\n" +
            "</WebPart>";
    }
}
