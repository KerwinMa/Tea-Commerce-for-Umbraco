using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.XPath;
using Autofac;
using System;
using System.Linq;
using System.Xml.Linq;
using TeaCommerce.Api.Common;
using TeaCommerce.Api.Dependency;
using TeaCommerce.Api.Models;
using TeaCommerce.Api.Services;
using TeaCommerce.Umbraco.Configuration.Services;
using TeaCommerce.Umbraco.Configuration.Variant.Product;
using umbraco;
using Umbraco.Core;
using Umbraco.Core.Dynamics;
using Umbraco.Core.Models;
using Umbraco.Web;
using Constants = TeaCommerce.Api.Constants;

namespace TeaCommerce.Umbraco.Configuration.InformationExtractors {
  public class ContentProductInformationExtractor : IContentProductInformationExtractor {

    protected readonly IStoreService StoreService;
    protected readonly ICurrencyService CurrencyService;
    protected readonly IVatGroupService VatGroupService;

    public static IContentProductInformationExtractor Instance { get { return DependencyContainer.Instance.Resolve<IContentProductInformationExtractor>(); } }

    public ContentProductInformationExtractor( IStoreService storeService, ICurrencyService currencyService, IVatGroupService vatGroupService ) {
      StoreService = storeService;
      CurrencyService = currencyService;
      VatGroupService = vatGroupService;
    }

    public virtual T GetPropertyValue<T>( IContent model, string propertyAlias, string variantGuid = null, Func<IContent, bool> func = null ) {
      T rtnValue = default( T );

      //TODO: H�ndter at modellen er null hvis noden ikke er publiseret, men s� har vi jo ikke noget id!!
      if ( model != null && !string.IsNullOrEmpty( propertyAlias ) ) {
        if ( !string.IsNullOrEmpty( variantGuid ) ) {
          IPublishedContent variant = null;
          long storeId = GetStoreId( model );

          variant = VariantService.Instance.GetVariant( storeId, model, variantGuid );

          if ( variant != null ) {
            rtnValue = variant.GetPropertyValue<T>( propertyAlias );
          }
        }
        if ( CheckNullOrEmpty( rtnValue ) ) {
          //Check if this node or ancestor has it
          IContent currentNode = func != null ? model.Ancestors().FirstOrDefault( func ) : model;
          if ( currentNode != null ) {
            rtnValue = GetPropertyValueInternal<T>( currentNode, propertyAlias, func == null );
          }

          //Check if we found the value
          if ( CheckNullOrEmpty( rtnValue ) ) {

            //Check if we can find a master relation
            string masterRelationNodeIdStr = GetPropertyValueInternal<string>( model, Constants.ProductPropertyAliases.MasterRelationPropertyAlias, true );
            int masterRelationNodeId = 0;
            if ( !string.IsNullOrEmpty( masterRelationNodeIdStr ) && int.TryParse( masterRelationNodeIdStr, out masterRelationNodeId ) ) {
              rtnValue = GetPropertyValue<T>( ApplicationContext.Current.Services.ContentService.GetById( masterRelationNodeId ), propertyAlias,
                variantGuid, func );
            }
          }

        }
      }

      return rtnValue;
    }

    protected virtual T GetPropertyValueInternal<T>( IContent content, string propertyAlias, bool recursive ) {
      T rtnValue = default( T );

      if ( content != null && !string.IsNullOrEmpty( propertyAlias ) ) {

        if ( !recursive ) {
          rtnValue = content.GetValue<T>( propertyAlias );
        } else {
          //We need to go recursive
          IContent tempModel = content;
          T tempProperty = default( T );
          try {
            tempProperty = tempModel.GetValue<T>( propertyAlias );
          } catch { }
          if ( !CheckNullOrEmpty( tempProperty ) ) {
            rtnValue = tempProperty;
          }

          while ( CheckNullOrEmpty( rtnValue ) && tempModel != null && tempModel.Id > 0 ) {
            tempModel = tempModel.Parent();
            if ( tempModel != null ) {
              try {
                tempProperty = tempModel.GetValue<T>( propertyAlias );
              } catch { }
              if ( !CheckNullOrEmpty( tempProperty ) ) {
                rtnValue = tempProperty;
              }
            }
          }
        }
      }

      return rtnValue;
    }



    public virtual long GetStoreId( IContent model ) {
      long? storeId = GetPropertyValue<long?>( model, Constants.ProductPropertyAliases.StorePropertyAlias );
      if ( storeId == null ) {
        throw new ArgumentException( "The model doesn't have a store id associated with it - remember to add the Tea Commerce store picker to your Umbraco content tree" );
      }

      return storeId.Value;
    }

    public virtual string GetSku( IContent model, string variantGuid = null ) {
      string sku = GetPropertyValue<string>( model, Constants.ProductPropertyAliases.SkuPropertyAlias, variantGuid );

      //If no sku is found - default to umbraco node id
      if ( string.IsNullOrEmpty( sku ) ) {
        sku = model.Id.ToString( CultureInfo.InvariantCulture ) + "_" + variantGuid;
      }

      return sku;
    }

    public virtual string GetName( IContent model, string variantGuid = null ) {
      string name = GetPropertyValue<string>( model, Constants.ProductPropertyAliases.NamePropertyAlias, variantGuid );

      //If no name is found - default to the umbraco node name
      if ( string.IsNullOrEmpty( name ) ) {
        name = model.Name;
      }

      return name;
    }

    public virtual long? GetVatGroupId( IContent model, string variantGuid = null ) {
      long storeId = GetStoreId( model );
      long? vatGroupId = GetPropertyValue<long?>( model, Constants.ProductPropertyAliases.VatGroupPropertyAlias, variantGuid );

      //In umbraco a product can have a relation to a delete marked vat group
      if ( vatGroupId != null ) {
        VatGroup vatGroup = VatGroupService.Get( storeId, vatGroupId.Value );
        if ( vatGroup == null || vatGroup.IsDeleted ) {
          vatGroupId = null;
        }
      }

      return vatGroupId;
    }

    public virtual long? GetLanguageId( IContent model ) {
      return LanguageService.Instance.GetLanguageIdByNodePath( model.Path );
    }

    public virtual OriginalUnitPriceCollection GetOriginalUnitPrices( IContent model, string variantGuid = null ) {
      OriginalUnitPriceCollection prices = new OriginalUnitPriceCollection();

      foreach ( Currency currency in CurrencyService.GetAll( GetStoreId( model ) ) ) {
        prices.Add( new OriginalUnitPrice( GetPropertyValue<string>( model, currency.PricePropertyAlias, variantGuid ).ParseToDecimal() ?? 0M, currency.Id ) );
      }

      return prices;
    }

    public virtual CustomPropertyCollection GetProperties( IContent model, string variantGuid = null ) {
      CustomPropertyCollection properties = new CustomPropertyCollection();

      foreach ( string productPropertyAlias in StoreService.Get( GetStoreId( model ) ).ProductSettings.ProductPropertyAliases ) {
        properties.Add( new CustomProperty( productPropertyAlias, GetPropertyValue<string>( model, productPropertyAlias, variantGuid ) ) { IsReadOnly = true } );
      }

      return properties;
    }

    public virtual ProductSnapshot GetSnapshot( IContent model, string productIdentifier ) {
      ProductIdentifier productIdentifierObj = new ProductIdentifier( productIdentifier );
      //We use Clone() because each method should have it's own instance of the navigator - so if they traverse it doesn't affect other methods
      ProductSnapshot snapshot = new ProductSnapshot( GetStoreId( model ), productIdentifier ) {
        Sku = GetSku( model, productIdentifierObj.VariantId ),
        Name = GetName( model, productIdentifierObj.VariantId ),
        VatGroupId = GetVatGroupId( model, productIdentifierObj.VariantId ),
        LanguageId = GetLanguageId( model ),
        OriginalUnitPrices = GetOriginalUnitPrices( model, productIdentifierObj.VariantId ),
        Properties = GetProperties( model, productIdentifierObj.VariantId )
      };

      return snapshot;
    }

    public virtual bool HasAccess( long storeId, IContent model ) {
      return storeId == GetStoreId( model ) && library.HasAccess( model.Id, model.Path );
    }

    private static bool CheckNullOrEmpty<T>( T value ) {
      if ( typeof( T ) == typeof( string ) )
        return string.IsNullOrEmpty( value as string );

      return value == null || value.Equals( default( T ) );
    }
  }
}
