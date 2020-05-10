using Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace IntegrationSprzedajemy
{
    /*
    * Based on:
    * - offer kind
    * - seller contact
    * - price
    * - address
    */
    public class Comparer : IEqualityComparer<Entry>
    {
        public bool Equals(Entry x, Entry y)
        {
            bool ret = true;
            ret &= x.OfferDetails.OfferKind == y.OfferDetails.OfferKind;

            ret &= x.OfferDetails.SellerContact.Telephone == y.OfferDetails.SellerContact.Telephone
                || x.OfferDetails.SellerContact.Email == y.OfferDetails.SellerContact.Email
                || x.OfferDetails.SellerContact.Name == y.OfferDetails.SellerContact.Name;

            ret &= x.PropertyPrice.TotalGrossPrice == y.PropertyPrice.TotalGrossPrice
                || x.PropertyPrice.PricePerMeter == y.PropertyPrice.PricePerMeter;

            ret &= x.PropertyAddress.City == y.PropertyAddress.City
                || x.PropertyAddress.District == y.PropertyAddress.District
                || x.PropertyAddress.StreetName == y.PropertyAddress.StreetName;

            return ret;
        }

        public int GetHashCode([DisallowNull] Entry obj)
        {
            int hash = 0;

            hash ^= obj.OfferDetails.OfferKind.GetHashCode();

            hash ^= obj.OfferDetails.SellerContact.Telephone.GetHashCode();
            hash ^= obj.OfferDetails.SellerContact.Email.GetHashCode();
            hash ^= obj.OfferDetails.SellerContact.Name.GetHashCode();

            hash ^= obj.PropertyPrice.TotalGrossPrice.GetHashCode();
            hash ^= obj.PropertyPrice.PricePerMeter.GetHashCode();

            hash ^= obj.PropertyAddress.City.GetHashCode();
            hash ^= obj.PropertyAddress.District.GetHashCode();
            hash ^= obj.PropertyAddress.StreetName.GetHashCode();
            
            return hash;
        }
    }
}
