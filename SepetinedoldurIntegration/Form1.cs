using SepetinedoldurIntegration.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace SepetinedoldurIntegration
{
    public partial class Form1 : Form
    {

        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string productXml = "";

            #region Read XML

            // var productXml = Server.MapPath("~/App_Data/products.xml");

            XmlDocument doc = new XmlDocument();
            doc.Load(productXml);

            var productsNodes = doc.SelectSingleNode("Urunler").SelectNodes("Urun");

            var n11Products = new List<Model.ProductModel>();
            for (int i = 0; i < productsNodes.Count; i++)
            {
                var productNode = productsNodes[i];
                var product = new ProductModel
                {
                    Id = productNode.SelectSingleNode("No").InnerText,
                    Name = productNode.SelectSingleNode("Adi").InnerText,
                    SKU = productNode.SelectSingleNode("BarkodNumarasi").InnerText,
                    Description = productNode.SelectSingleNode("Aciklama").InnerText,
                    ShortDescription = productNode.SelectSingleNode("Model").InnerText,
                    Price = Convert.ToDecimal(productNode.SelectSingleNode("SonKullaniciFiyati").InnerText.Replace(",", ".")),
                    Stock = productNode.SelectSingleNode("Stok").InnerText,
                };

                if (productNode.SelectSingleNode("SonKullaniciFiyati").InnerText != "0.0000")//sıfır değilse indirimli fiyatı mevcut
                {
                    product.Price = Convert.ToDecimal(productNode.SelectSingleNode("SonKullaniciFiyati").InnerText.Replace(",", "."));//Orjinal fiyat
                    product.DiscountPrice = Convert.ToDecimal(productNode.SelectSingleNode("SonKullaniciFiyati").InnerText.Replace(",", "."));//İndirimli fiyat
                }

                if (productNode.SelectSingleNode("ProductAttributes").SelectNodes("ProductAttributeMapping").Count > 0)//ürünün opsiyonları Beden,Renk, Ayakkabı numarası gibi
                {
                    var productMappings = productNode.SelectSingleNode("ProductAttributes").SelectNodes("ProductAttributeMapping");
                    for (int j = 0; j < productMappings.Count; j++)
                    {
                        var productOptionValues = productMappings[j].SelectSingleNode("ProductAttributeValues").SelectNodes("ProductAttributeValue");
                        for (int k = 0; k < productOptionValues.Count; k++)
                        {
                            product.Options.Add(new ProductOption
                            {
                                Name = productMappings[j].SelectSingleNode("ProductAttributeName").InnerText,
                                Value = productOptionValues[k].SelectSingleNode("Name").InnerText,
                                Price = Convert.ToDecimal(productOptionValues[k].SelectSingleNode("PriceAdjustment").InnerText.Replace(",", ".")),
                                StockQuantity = Convert.ToInt32(productOptionValues[k].SelectSingleNode("Quantity").InnerText)
                            });
                        }
                    }
                }
                n11Products.Add(product);
            }
            #endregion

            #region Send N11

            var authentication = new N11ProductService.Authentication()
            {
                appKey = "", //api anahtarınız
                appSecret = ""//api şifeniz
            };

            int succesProductCount = 0;
            int errorProductCount = 0;
            string errorMessages = string.Empty;

            foreach (var product in n11Products)
            {
                #region Pictures
                var productImages = new List<N11ProductService.ProductImage>();

                foreach (var item in productImages)
                {
                    productImages.Add(new N11ProductService.ProductImage { url = item.url, order = item.order });
                }


                #endregion

                #region Options & Properties
                //ayakkabı/t-shirt
                //Beden va ayakkabı numaralarında her kırılıma bir stockitem oluşturup içerisine kırılımı ve kırılımın stoğunu ekliyoruz ve sellerCode barkod oluyor
                var stockItems = new List<N11ProductService.ProductSkuRequest>();
                foreach (var item in product.Options)
                {
                    stockItems.Add(new N11ProductService.ProductSkuRequest
                    {
                        attributes = new N11ProductService.ProductAttributeRequest[] { new N11ProductService.ProductAttributeRequest { name = item.Name, value = item.Value } },
                        optionPrice = product.Price,
                        quantity = item.StockQuantity.ToString(),
                        sellerStockCode = item.Barcode
                    });
                }

                //çanta
                //eğer stokitem listesi boş ise bu çanta/kalemlik vs. oluyor, tek bir stockitem oluşturup fiyat, adet ve stockCode veriyoruz.
                //barkod boş gelebiliyor bu yüzden kontrol edip boş ise SKU veriyoruz
                if (stockItems.Count <= 0)
                {
                    stockItems.Add(new N11ProductService.ProductSkuRequest
                    {
                        optionPrice = product.Price,
                        quantity = product.Stock,
                        sellerStockCode = string.IsNullOrEmpty(product.Barcode) ? product.SKU : product.Barcode
                    });
                }
                #endregion

                #region Discont & Category
                //1 = İndirim Tutarı Cinsinden
                //2 = İndirim Oranı Cinsinden
                //3 = İndirimli Fiyat Cinsinden
                var discountRequest = new N11ProductService.ProductDiscountRequest
                {
                    type = "3",
                    value = product.DiscountPrice.ToString().Replace(",", ".")
                };

                #endregion

                //Ürün kısa açıklaması boş gelebiliyor
                if (string.IsNullOrEmpty(product.ShortDescription))
                    product.ShortDescription = product.Name;

                #region Product Request
                var productRequest = new N11ProductService.ProductRequest
                {
                    productSellerCode = product.Id,
                    title = product.Name,
                    subtitle = product.ShortDescription.Substring(0, 5),
                    description = product.Description,
                    category = new N11ProductService.CategoryRequest
                    {
                        id = 1356//N11 kategorilerinden ürününüze uygun olan kategorinin idsini buraya ekliyoruz
                    },
                    price = product.Price,
                    currencyType = "1",//TL 1
                    images = productImages.Take(8).ToArray(),//n11 en fazla 8 ürün görseli kabul ediyor
                    approvalStatus = "1",//Aktif 1 | Pasif 0
                    preparingDay = "3",//Hazırlanma süresi
                    stockItems = stockItems.ToArray(),
                    discount = product.DiscountPrice > 0 ? discountRequest : null,
                    productCondition = "1",//Yeni ürün 1 | İkinci el 2
                    shipmentTemplate = "Depo",//Bunun n11 panelinizden teslimat şablonları bölümünden oluşturabilirsiniz.
                };

                var request = new N11ProductService.SaveProductRequest
                {
                    product = productRequest,
                    auth = authentication
                };

                var servicePort = new N11ProductService.ProductServicePortClient();
                #endregion

                var response = servicePort.SaveProduct(request);
                if (response.result != null)
                {
                    if (response.result.status.Contains("success"))
                        succesProductCount++;

                    if (!string.IsNullOrEmpty(response.result.errorMessage))
                    {
                        errorProductCount++;
                        errorMessages += string.Format("SKU : {0}, Hata Detayı : {1}<br />", product.SKU, response.result.errorMessage);
                    }
                }
            }

            string result = "Gönderilen Ürün : {0}, Hatalı Ürün : {1}<br /><br />Hata Mesajları : {2}";

            // return Content(string.Format(result, succesProductCount, errorProductCount, errorMessages), "text/html");
            // result
            // success product count
            // error product count
            // error messages
            // text/html

            // hatalar
            string content = string.Format(result, succesProductCount, errorProductCount, errorMessages);

            #endregion
        }

        // kategorileri çeker
        private void button2_Click(object sender, EventArgs e)
        {
            var authentication = new N11CategoryService.Authentication();
            authentication.appKey = ""; //api anahtarınız
            authentication.appSecret = "";//api şifeniz

            N11CategoryService.CategoryServicePortClient proxy = new N11CategoryService.CategoryServicePortClient();

            var request = new N11CategoryService.GetTopLevelCategoriesRequest();
            request.auth = authentication;
            var categories = proxy.GetTopLevelCategories(request).categoryList;
            List<CategoryModel> myCategories = new List<CategoryModel>();
            foreach (var item in categories)
            {
                myCategories.Add(
                    new CategoryModel
                    {
                        Id = item.id,
                        Name = item.name
                    }
                );

            }

            new CategoriesForm(myCategories).Show();

        }
    }
}
