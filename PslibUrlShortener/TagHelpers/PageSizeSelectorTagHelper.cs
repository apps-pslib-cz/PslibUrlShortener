using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Text.Encodings.Web;

namespace PslibUrlShortener.TagHelpers
{
    [HtmlTargetElement("pagesize-selector")]
    public class PageSizeSelectorTagHelper : TagHelper
    {
        private readonly IUrlHelperFactory _urlHelperFactory;

        public PageSizeSelectorTagHelper(IUrlHelperFactory urlHelperFactory)
        {
            _urlHelperFactory = urlHelperFactory;
        }

        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = default!;

        /// <summary>Aktuální velikost stránky</summary>
        public int PageSize { get; set; }

        /// <summary>Nabízené možnosti</summary>
        public IEnumerable<int> Sizes { get; set; } = new[] { 10, 20, 50, 100 };

        /// <summary>Aktuální číslo stránky – reset na 1 při změně</summary>
        public int PageNo { get; set; }

        /// <summary>Cesta k page</summary>
        public string Page { get; set; } = "./Index";

        /// <summary>Dodatečné route parametry (filtry, řazení…)</summary>
        public IDictionary<string, string?> RouteValues { get; set; } = new Dictionary<string, string?>();

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var urlHelper = _urlHelperFactory.GetUrlHelper(ViewContext);

            output.TagName = "form";
            output.TagMode = TagMode.StartTagAndEndTag;               // důležité
            output.Attributes.SetAttribute("method", "get");
            output.AddClass("d-inline-block", HtmlEncoder.Default);

            var group = new TagBuilder("div");
            group.AddCssClass("input-group input-group-sm");

            var label = new TagBuilder("label");
            label.AddCssClass("visually-hidden");
            label.Attributes["for"] = "PageSize";
            label.InnerHtml.Append("Počet položek na stránku");
            group.InnerHtml.AppendHtml(label);

            var select = new TagBuilder("select");
            select.AddCssClass("form-select form-select-sm");
            select.Attributes["id"] = "PageSize";
            select.Attributes["name"] = "PageSize";
            select.Attributes["aria-label"] = "Počet položek na stránku";
            select.Attributes["onchange"] = "this.form.submit()";

            foreach (var size in Sizes)
            {
                var option = new TagBuilder("option");
                option.Attributes["value"] = size.ToString();
                if (size == PageSize)
                    option.Attributes["selected"] = "selected";
                option.InnerHtml.Append(size.ToString());
                select.InnerHtml.AppendHtml(option);
            }

            group.InnerHtml.AppendHtml(select);

            var button = new TagBuilder("button");
            button.AddCssClass("btn btn-sm btn-primary");
            button.Attributes["type"] = "submit";
            button.InnerHtml.Append("Změnit");
            group.InnerHtml.AppendHtml(button);

            // hidden inputs (pro zachování filtrování/řazení)
            foreach (var kv in RouteValues)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                var hidden = new TagBuilder("input");
                hidden.Attributes["type"] = "hidden";
                hidden.Attributes["name"] = kv.Key;
                hidden.Attributes["value"] = kv.Value ?? "";
                group.InnerHtml.AppendHtml(hidden);
            }

            // reset PageNo při změně
            var pageNoInput = new TagBuilder("input");
            pageNoInput.Attributes["type"] = "hidden";
            pageNoInput.Attributes["name"] = "PageNo";
            pageNoInput.Attributes["value"] = "1";
            group.InnerHtml.AppendHtml(pageNoInput);

            output.Content.AppendHtml(group);
        }
    }
}
