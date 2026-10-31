using FHG.UI;
using System;
using System.IO;
using System.Linq;
var item = new SubscriptionItem { Name="Sample" };
SubscriptionStore.ApplyHeader(item,"subscription-userinfo","upload=1073741824; download=2147483648; total=10737418240; expire=1798761600");
if(item.Usage!="3 GiB / 10 GiB" || item.Remaining!="7 GiB" || item.ExpireUnix!=1798761600)
  throw new Exception("Normal quota parsing failed: "+item.Usage+" | "+item.Remaining);
var unlimited = new SubscriptionItem();
SubscriptionStore.ApplyHeader(unlimited,"subscription-userinfo","upload=12345; download=67890");
if(unlimited.Remaining!="Unlimited" || unlimited.Usage.Contains(" / 0 GiB"))
  throw new Exception("Missing total should be unlimited");
var zero = new SubscriptionItem();
SubscriptionStore.ApplyHeader(zero,"subscription-userinfo","upload=12345; download=67890; total=0; expire=0");
if(zero.Remaining!="Unlimited" || zero.Expiry!="Not provided")
  throw new Exception("Explicit zero limits should not be exhausted");
var title = new SubscriptionItem();
SubscriptionStore.ApplyHeader(title,"profile-title","base64:SGVsbG8=");
if(title.Name!="Hello") throw new Exception("Encoded title failed");
var root=Path.Combine(Path.GetTempPath(),"fhg-sub-test-"+Guid.NewGuid().ToString("N"));
try{
 var store=new SubscriptionStore(root);
 var sub=store.Add("https://example.invalid/secret-token");
 if(sub.Name.Contains("secret-token")) throw new Exception("Token leaked in display name");
 if(store.Items.Count!=1) throw new Exception("No saved profile");
 var reload=new SubscriptionStore(root);
 if(reload.Items.Count!=1 || reload.Items[0].Source!=sub.Source) throw new Exception("Profile persistence failed");
 reload.Remove(reload.Items[0]);
 if(reload.Items.Count!=0) throw new Exception("Profile deletion failed");
}finally{Directory.Delete(root,recursive:true);}
Console.WriteLine("PASS: subscription parsing, unlimited traffic, encoded titles, persistence, secret-safe display");
