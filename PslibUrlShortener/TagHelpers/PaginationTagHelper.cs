using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PslibUrlShortener.TagHelpers
{
    [HtmlTargetElement("pagination")]
    public class PaginationTagHelper : TagHelper
    {
        private readonly IUrlHelperFactory _urlHelperFactory;

        public PaginationTagHelper(IUrlHelperFactory urlHelperFactory)
        {
            _urlHelperFactory = urlHelperFactory;
        }

        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = default!;

        /// <summary>Aktuální číslo stránky</summary>
        public int PageNo { get; set; }

        /// <summary>Velikost stránky</summary>
        public int PageSize { get; set; }

        /// <summary>Počet všech záznamů</summary>
        public int TotalCount { get; set; }

        /// <summary>Okno kolem současné stránky (např. 2 znamená +-2 stránky)</summary>
        public int Window { get; set; } = 2;

        /// <summary>Cesta k page (např. "./Index")</summary>
        public string Page { get; set; } = "./Index";

        /// <summary>Dodatečné route parametry (např. filtr, řazení)</summary>
        public IDictionary<string, string?> RouteValues { get; set; } = new Dictionary<string, string?>();

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var urlHelper = _urlHelperFactory.GetUrlHelper(ViewContext);
            int totalPages = (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));

            if (totalPages <= 1)
            {
                output.SuppressOutput();
                return;
            }

            output.TagName = "nav";
            output.TagMode = TagMode.StartTagAndEndTag;                  // důležité
            output.Attributes.SetAttribute("aria-label", "Stránkování");
            output.Attributes.SetAttribute("role", "navigation");

            var ul = new TagBuilder("ul");
            ul.AddCssClass("pagination mb-0");

            // ---- Předchozí
            ul.InnerHtml.AppendHtml(CreatePageItem("Předchozí",
                PageNo > 1 ? PageNo - 1 : 1,
                isActive: false,
                isDisabled: PageNo == 1, urlHelper));

            // ---- první vs. "…"
            if (PageNo - Window > 1)
            {
                ul.InnerHtml.AppendHtml(CreatePageItem("1", 1, isActive: false, isDisabled: false, urlHelper));
                ul.InnerHtml.AppendHtml(CreateEllipsis());
            }

            // ---- okno kolem aktuální
            for (int i = Math.Max(1, PageNo - Window);
                 i <= Math.Min(totalPages, PageNo + Window); i++)
            {
                ul.InnerHtml.AppendHtml(CreatePageItem(i.ToString(), i, isActive: PageNo == i, isDisabled: false, urlHelper));
            }

            // ---- poslední vs. "…"
            if (PageNo + Window < totalPages)
            {
                ul.InnerHtml.AppendHtml(CreateEllipsis());
                ul.InnerHtml.AppendHtml(CreatePageItem(totalPages.ToString(), totalPages, isActive: false, isDisabled: false, urlHelper));
            }

            // ---- Další
            ul.InnerHtml.AppendHtml(CreatePageItem("Další",
                PageNo < totalPages ? PageNo + 1 : totalPages,
                isActive: false,
                isDisabled: PageNo == totalPages, urlHelper));

            output.Content.AppendHtml(ul);
        }

        private TagBuilder CreatePageItem(string text, int pageNo, bool isActive, bool isDisabled, IUrlHelper urlHelper)
        {
            var li = new TagBuilder("li");
            li.AddCssClass("page-item");
            if (isActive) li.AddCssClass("active");
            if (isDisabled) li.AddCssClass("disabled");

            var isNumeric = text.All(char.IsDigit);

            if (isActive && isNumeric)
            {
                // Aktuální stránka – span s aria-current
                var span = new TagBuilder("span");
                span.AddCssClass("page-link");
                span.Attributes["aria-current"] = "page";
                span.InnerHtml.Append(text);
                li.InnerHtml.AppendHtml(span);
                return li;
            }

            var a = new TagBuilder("a");
            a.AddCssClass("page-link");
            a.InnerHtml.Append(text);

            if (isDisabled)
            {
                a.Attributes["tabindex"] = "-1";
                a.Attributes["aria-disabled"] = "true";
            }

            if (text == "Předchozí") a.Attributes["rel"] = "prev";
            if (text == "Další") a.Attributes["rel"] = "next";

            // slož URL se zachováním route values
            var allRoutes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in RouteValues)
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !allRoutes.ContainsKey(kvp.Key))
                    allRoutes[kvp.Key] = kvp.Value;

            allRoutes["PageNo"] = pageNo;
            allRoutes["PageSize"] = PageSize;

            a.Attributes["href"] = urlHelper.Page(Page, allRoutes);
            li.InnerHtml.AppendHtml(a);
            return li;
        }

        private TagBuilder CreateEllipsis()
        {
            var li = new TagBuilder("li");
            li.AddCssClass("page-item disabled");

            var span = new TagBuilder("span");
            span.AddCssClass("page-link");
            span.Attributes["aria-hidden"] = "true";
            span.InnerHtml.Append("…");

            li.InnerHtml.AppendHtml(span);
            return li;
        }
    }
}
