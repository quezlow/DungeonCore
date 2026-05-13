using UnityEngine;
using UnityEngine.UI;

public class TabController : MonoBehaviour
{
    public Image[] tabImages;
    public GameObject[] pages;

    void Start()
    {
        //Open menu to first page number, comment out to open at last menu page
        //ActivateTab(0);
    }

    public void ActivateTab(int tabNum)
    {
        for(int i = 0; i < pages.Length; i++)
        {
            pages[i].SetActive(false);
            tabImages[i].color = Color.gray;
        }
        pages[tabNum].SetActive(true);
        tabImages[tabNum].color = Color.white;
    }
}
