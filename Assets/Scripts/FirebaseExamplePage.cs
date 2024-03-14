using Firebase.Extensions;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Xsolla.Catalog;
using Xsolla.Core;

[Serializable]
public class UserData
{
    public Data data;

    [Serializable]
    public class Data
    {
        public string uid;
        public string email;
        public string sku;
        public string returnUrl;
    }
}

public class FirebaseExamplePage : MonoBehaviour
{
    public GameObject LoginContainer;
    public GameObject StoreItemsContainer;

    public InputField EmailInputField;
    public InputField PasswordInputField;
    public Button LoginButton;
    public Button RegisterButton;

    public Transform WidgetsContainer;
    public GameObject WidgetPrefab;

    protected Firebase.Auth.FirebaseAuth auth;
    Firebase.Auth.FirebaseUser user = null;

    Firebase.DependencyStatus dependencyStatus = Firebase.DependencyStatus.UnavailableOther;

    public virtual void Start()
    {
        Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
            dependencyStatus = task.Result;
            if (dependencyStatus == Firebase.DependencyStatus.Available)
            {
                InitializeFirebase();
            }
            else
            {
                Debug.LogError(
                  "Could not resolve all Firebase dependencies: " + dependencyStatus);
            }
        });
    }

    protected void InitializeFirebase()
    {
        StoreItemsContainer.SetActive(false);

        Debug.Log("Setting up Firebase Auth");
        auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        auth.StateChanged += AuthStateChanged;
        RegisterButton.onClick.AddListener(() =>
        {
            auth.CreateUserWithEmailAndPasswordAsync(EmailInputField.text, PasswordInputField.text).ContinueWith(task =>
            {
                if (task.IsCanceled)
                {
                    Debug.LogError("CreateUserWithEmailAndPasswordAsync was canceled.");
                    return;
                }
                if (task.IsFaulted)
                {
                    Debug.LogError("CreateUserWithEmailAndPasswordAsync encountered an error: " + task.Exception);
                    return;
                }

                Firebase.Auth.AuthResult result = task.Result;
                Debug.LogFormat("Firebase user created successfully: {0} ({1})",
                    result.User.DisplayName, result.User.UserId);
            });
        });

        LoginButton.onClick.AddListener(() =>
        {
            auth.SignInWithEmailAndPasswordAsync(EmailInputField.text, PasswordInputField.text).ContinueWith(task =>
            {
                if (task.IsCanceled)
                {
                    Debug.LogError("SignInWithEmailAndPasswordAsync was canceled.");
                    return;
                }
                if (task.IsFaulted)
                {
                    Debug.LogError("SignInWithEmailAndPasswordAsync encountered an error: " + task.Exception);
                    return;
                }

                Firebase.Auth.AuthResult result = task.Result;
                Debug.LogFormat("Firebase user logged in successfully: {0} ({1})",
                    result.User.DisplayName, result.User.UserId);
            });
        });
    }

    void AuthStateChanged(object sender, System.EventArgs eventArgs)
    {
        Firebase.Auth.FirebaseAuth senderAuth = sender as Firebase.Auth.FirebaseAuth;
        if (senderAuth == auth && senderAuth.CurrentUser != user)
        {
            bool signedIn = user != senderAuth.CurrentUser && senderAuth.CurrentUser != null;
            if (!signedIn && user != null)
            {
                Debug.Log("Signed out " + user.UserId);
            }
            user = senderAuth.CurrentUser;
            if (signedIn)
            {
                Debug.Log("AuthStateChanged Signed in " + user.UserId);
                LoadCatalog();
            }
        }
    }

    void OnDestroy()
    {
        if (auth != null)
        {
            auth.SignOut();
            auth.StateChanged -= AuthStateChanged;
            auth = null;
        }
    }
    private void LoadCatalog()
    {
        LoginContainer.SetActive(false);
        StoreItemsContainer.SetActive(true);
        XsollaCatalog.GetCatalog(OnItemsRequestSuccess, OnError);
    }

    private void OnItemsRequestSuccess(StoreItems storeItems)
    {

        foreach (var storeItem in storeItems.items)
        {
            var widgetGo = Instantiate(WidgetPrefab, WidgetsContainer, false);
            var widget = widgetGo.GetComponent<StoreItemWidget>();

            if(widget != null)
            {
                widget.NameText.text = storeItem.name;
                widget.DescriptionText.text = storeItem.description;

                widget.BuyButton.onClick.AddListener(() =>
                {
                    StartCoroutine(MakeCloudFunctionRequest(storeItem.sku));
                });

                if (storeItem.price != null)
                {
                    var realMoneyPrice = storeItem.price;
                    widget.PriceText.text = $"{realMoneyPrice.amount} {realMoneyPrice.currency}";
                }

                ImageLoader.LoadSprite(storeItem.image_url, sprite => widget.IconImage.sprite = sprite);
            }
        }
    }
    IEnumerator MakeCloudFunctionRequest(string sku)
    {
        string url = "http://127.0.0.1:5001/<your firebase project id>/us-central1/getXsollaPaymentToken";

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            var userData = new UserData()
            {
                data = new UserData.Data() {
                    uid = user.UserId,
                    email = user.Email,
                    sku = sku,
                    returnUrl = "app://xpayment.com.xsolla.unitysample"
                }
            };

            byte[] data = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(userData, true));
            UploadHandlerRaw upHandler = new UploadHandlerRaw(data);
            upHandler.contentType = "application/json";
            webRequest.uploadHandler = upHandler;
            webRequest.method = "POST";
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + webRequest.error);
            }
            else
            {
                string responseJson = webRequest.downloadHandler.text;
                var responseData = JsonUtility.FromJson<OrderData>(responseJson);

                var paymentToken = responseData.token;
                int orderId = responseData.order_id;

                XsollaWebBrowser.OpenPurchaseUI(
                        paymentToken,
                        false);
                Debug.Log("Response: " + webRequest.downloadHandler.text);
            }
        }
    }

    private void OnError(Error error)
    {
        Debug.LogError($"Error: {error.errorMessage}");
    }
}
