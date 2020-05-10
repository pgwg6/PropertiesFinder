using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Interfaces;
using Models;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace IntegrationSprzedajemy
{
    public class Integration : IWebSiteIntegration
    {
        public WebPage WebPage { get; }
        public IDumpsRepository DumpsRepository { get; }

        public IEqualityComparer<Entry> EntriesComparer { get; }

        private List<string> offers = new List<string>();

        public Integration(IDumpsRepository dumpsRepository,
            IEqualityComparer<Entry> equalityComparer)
        {
            DumpsRepository = dumpsRepository;
            EntriesComparer = equalityComparer;
            WebPage = new WebPage
            {
                Url = "https://sprzedajemy.pl/nieruchomosci/mieszkania",
                Name = "sprzedajemy.pl integration",
                WebPageFeatures = new WebPageFeatures
                {
                    HomeSale = false,
                    HomeRental = false,
                    HouseSale = false,
                    HouseRental = false
                }
            };
        }

        public Dump GenerateDump()
        {
            RetrieveAllOffers();

            var entriesTasks = new List<Task<Entry>>();
            var entriesDone = new List<Entry>();
            
            foreach (var o in offers)
            {
                entriesTasks.Add(Task.Run(() => RetriveEntry(o)));

                // Note : As long as HtmlAgilityPack.HtmlWeb.Load is a bottleneck multithreading is disabled
                entriesTasks.Last().Wait();
            }

            foreach (var t in entriesTasks)
            {
                t.Wait();
                entriesDone.Add(t.Result);
            }

            return new Dump
            {
                DateTime = DateTime.Now,
                WebPage = WebPage,
                Entries = entriesDone
            };
        }

        private void RetrieveAllOffers()
        {
            // Warning : without any filters it is possible to scrap up to (15'000 + offersPerPage) offers

            var maxPages = 251;
            var offersPerPage = 60;
            var pageParams = "{0}?items_per_page={1}&offset={2}";

            for(int i = 0; i < maxPages; i++)
            {
                string url = string.Format(pageParams, WebPage.Url, offersPerPage, i * offersPerPage);
                AppendOffersUrls(url);
            }
        }
        private void AppendOffersUrls(string url)
        {
            HtmlWeb web = new HtmlWeb();
            HtmlDocument doc = web.Load(url);

            var content = doc.DocumentNode.SelectSingleNode(XPathHelper.GetElementByClass("div", "cntListBody"));

            var titles = content.SelectNodes(XPathHelper.GetElementByClass("h2", "title"));


            foreach (var t in titles)
            {
                offers.Add(t.ChildNodes["a"].Attributes["href"].Value);
            }
        }

        private Entry RetriveEntry(string url)
        {
            Console.WriteLine($"Processing {url}");
            try
            {
                /*
                 *  Hierarchy brief:
                 *  doc
                 *      offerAdditionalInfo
                 *      detailedInformations
                 *          attributes-box
                 *      offerDetailsAdditional
                 *          additionalInfoBox
                 *              detailedInfo
                 *          priceWrp
                 */

                // Bottleneck : HtmlAgilityPack.HtmlWeb.Load looks like signlethread method (very inefficient package to load page from internet
                // Possible optimization : `smart/html` CURL on each thread and then parse from memory to HtmlDocument
                HtmlWeb web = new HtmlWeb();
                HtmlDocument doc = web.Load($"{WebPage.Url}{url}");

                var offerAdditionalInfoNode = doc.DocumentNode.SelectSingleNode(XPathHelper.GetElementByClass("ul", "offerAdditionalInfo"));
                var detailedInformationsNode = doc.DocumentNode.SelectSingleNode(XPathHelper.GetElementByClass("div", "detailedInformations"));
                var attributesBoxNode = detailedInformationsNode.SelectSingleNode(XPathHelper.GetElementByClass("div", "attributes-box"));
                var offerDetailsAdditionalNode = doc.DocumentNode.SelectSingleNode(XPathHelper.GetElementByClass("section", "offerDetailsAdditional"));
                var additionalInfoBoxNode = offerDetailsAdditionalNode.SelectSingleNode(XPathHelper.GetElementByClass("div", "additionalInfoBox"));
                var detailedInfoNode = additionalInfoBoxNode.SelectSingleNode(XPathHelper.GetElementByClass("div", "detailedInfo"));
                var priceWrpNode = offerDetailsAdditionalNode.SelectSingleNode(XPathHelper.GetElementByClass("div", "priceWrp"));

                Entry e = new Entry
                {
                    OfferDetails = new OfferDetails
                    {
                        Url = url,
                        IsStillValid = true, // only valid offers in browser
                        OfferKind = OfferKind.SALE, // only sales on that page
                        SellerContact = GetSellerContact(additionalInfoBoxNode),
                        CreationDateTime = GetCreationDateTime(offerAdditionalInfoNode)
                    },
                    PropertyAddress = GetPropertyAddress(detailedInfoNode, offerAdditionalInfoNode),
                    PropertyDetails = GetPropertyDetails(attributesBoxNode),
                    PropertyFeatures = new PropertyFeatures(), // not unified data, error's level too high
                    PropertyPrice = GetPropertyPrice(priceWrpNode),
                    RawDescription = detailedInformationsNode.InnerText
                };

                Console.WriteLine($"Ended {url}");
                return e;
            }
            catch
            {
                Console.WriteLine($"Error in {url}");
                return new Entry();
            }
        }
        private SellerContact GetSellerContact(HtmlNode node)
        {
            string name = node.SelectSingleNode(XPathHelper.GetElementByClass("strong", "name")).InnerText;

            var phoneNumberTruncated = node.SelectSingleNode(XPathHelper.GetElementByClass("span", "phone-number-truncated"));
            string phone = phoneNumberTruncated != null
                ? $"{phoneNumberTruncated.ChildNodes["span"].InnerText} {phoneNumberTruncated.Attributes["data-phone-end"].Value}"
                : "";

            return new SellerContact
            {
                Email = "", // no emails on page, only in-service messages
                Name = name,
                Telephone = phone.Trim()
            };
        }

        private readonly Regex DateTimeTodayRegex = new Regex(@"Dzisiaj (\d{2}):(\d{2})");
        private readonly Regex DateTimeYesterdayRegex = new Regex(@"Wczoraj (\d{2}):(\d{2})");
        private readonly Regex DateTimeRegularRegex = new Regex(@"(\d{2}) (.{3}) (\d{2}):(\d{2})");
        private readonly Dictionary<string, int> DateTimeMonthsBindings = new Dictionary<string, int>
        {
            { "Sty", 1 }, { "Lut", 2 }, { "Mar", 3 }, { "Kwi", 4 }, { "Maj", 5 }, { "Cze", 6 },
            { "Lip", 7 }, { "Sie", 8 }, { "Wrz", 9 }, { "Paź", 10 }, { "Lis", 11 }, { "Gru", 12 }
        };

        private DateTime GetCreationDateTime(HtmlNode node)
        {
            // Warning : Year is not tracked
            // Warning : side effect on default return - should be resolved wheater null or current date time will be returned

            var dateTimeNode = node.SelectSingleNode(XPathHelper.GetElementByClass("i", "icon icon-clock")).ParentNode;
            var dateTimeStr = dateTimeNode.InnerText;

            var todayMatch = DateTimeTodayRegex.Match(dateTimeStr);
            if (todayMatch.Success)
            {
                return new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day,
                    int.Parse(todayMatch.Groups[1].Value), int.Parse(todayMatch.Groups[2].Value), 0);
            }

            var yesterdayMatch = DateTimeYesterdayRegex.Match(dateTimeStr);
            if (yesterdayMatch.Success)
            {
                return new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day - 1,
                    int.Parse(yesterdayMatch.Groups[1].Value), int.Parse(yesterdayMatch.Groups[2].Value), 0);
            }

            var regularMatch = DateTimeRegularRegex.Match(dateTimeStr);
            if (regularMatch.Success)
            {
                return new DateTime(DateTime.Now.Year, DateTimeMonthsBindings[regularMatch.Groups[2].Value], int.Parse(regularMatch.Groups[1].Value),
                    int.Parse(regularMatch.Groups[3].Value), int.Parse(regularMatch.Groups[4].Value), 0);
            }

            return DateTime.Now;
        }

        private static decimal GetOnlyNumber(string str)
        {
            var p = new string(str.Where(c => char.IsDigit(c) || c == '.').ToArray());
            return decimal.TryParse(p, out decimal d) ? d : 0;
        }

        private PropertyPrice GetPropertyPrice(HtmlNode node)
        {
            var priceStr = node.SelectSingleNode(XPathHelper.GetElementByClass("strong", "price ")).InnerText;
            var pricePerMeterNode = node.SelectSingleNode(XPathHelper.GetElementByClass("span", "pricePerMeter"));

            // Note : page contains only rounded prices
            return new PropertyPrice
            {
                TotalGrossPrice = GetOnlyNumber(priceStr),
                PricePerMeter = pricePerMeterNode != null ? GetOnlyNumber(pricePerMeterNode.InnerText) : 0,
                ResidentalRent = null
            };
        }

        private readonly Dictionary<char, char> PolishUpperDiacriticsMapping = new Dictionary<char, char>
        {
            {'Ą', 'A'}, {'Ć', 'C'}, {'Ę', 'E'}, {'Ł', 'L'}, {'Ń', 'N'}, {'Ó', 'O'}, {'Ś', 'S'}, {'Ż', 'Z'}, {'Ź', 'Z'}
        };

        private PropertyAddress GetPropertyAddress(HtmlNode detailInfoNode, HtmlNode offerAdditionalInfoNode)
        {
            var city = detailInfoNode.SelectSingleNode(XPathHelper.GetElementByClass("span", "locationName trunc")).InnerText.ToUpper();
            var locationName = offerAdditionalInfoNode.SelectSingleNode(XPathHelper.GetElementByClass("a", "locationName"));
            var district = new string(locationName.InnerText.Remove(0, city.Length).TakeWhile(c => c != ',').ToArray());
            var street = locationName.Attributes["data-details"]?.Value;

            foreach (var ch in PolishUpperDiacriticsMapping)
            {
                city = city.Replace(ch.Key, ch.Value);
            }
            city = city.Replace(' ', '_');

            var coords = detailInfoNode.SelectSingleNode(XPathHelper.GetElementByClass("div", "user-contact-item location clickable"));
            string detailed = coords != null ? coords.Attributes["data-coordinates"].Value : "";

            return new PropertyAddress
            {
                City = Enum.TryParse(typeof(PolishCity), city, out object c) ? (PolishCity)c : new PolishCity(),
                DetailedAddress = detailed,
                District = district,
                StreetName = street
            };
        }

        private PropertyDetails GetPropertyDetails(HtmlNode node)
        {
            var ret = new PropertyDetails();
            var childs = node.SelectSingleNode(XPathHelper.GetElementByClass("ul", "attribute-list")).ChildNodes;

            foreach (var e in childs)
            {
                if (e.Name != "li") continue;

                var key = e.ChildNodes["span"]?.InnerText.Trim();
                var value = e.ChildNodes["strong"]?.InnerText.Trim();

                if (key == "Powierzchnia")
                {
                    ret.Area = GetOnlyNumber(value);
                }
                else if (key == "Liczba pokoi")
                {
                    ret.NumberOfRooms = int.TryParse(value, out int v) ? v : 0;
                }
                else if (key == "Piętro")
                {
                    ret.FloorNumber = int.TryParse(value, out int v) ? (int?)v : null;
                }
                else if (key == "Rok budowy")
                {
                    ret.YearOfConstruction = int.TryParse(value, out int v) ? (int?)v : null;
                }
            }

            return ret;
        }
    }

    class XPathHelper
    {
        public static String GetElementByClass(string element, string className)
        {
            return $"//{element}[@class=\"{className}\"]";
        }

        public static String GetUrlFromChild()
        {
            return $"/a[@href]";
        }
    }
}
