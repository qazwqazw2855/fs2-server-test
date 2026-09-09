# God2PacketCapture

## 正式遊戲流程

1. 在工具中選擇正式遊戲目錄的 `Launcher.exe`。
2. 按「一鍵開始抓封包」，需要時完成 UAC。
3. 在 Launcher.exe 按「開始遊戲」。
4. Launcher.exe 啟動 `God2_opt.exe` 後，請在 `God2_opt.exe` 的遊戲登入畫面輸入帳號密碼。
5. 在 God2_opt.exe 完成伺服器群組、角色選擇並進入世界。
6. 完成觀測後按「一鍵停止抓包」，等待 ETL、PCAPNG、SQLite、JSONL 與 CSV Flush。

不要直接啟動 `God2_opt.exe`，也不要在 Launcher.exe 尋找帳號密碼欄位。

簡體中文：Launcher.exe 启动 God2_opt.exe 后，请在 God2_opt.exe 的游戏登录界面输入账号密码。

English: After Launcher.exe starts God2_opt.exe, enter the account credentials in the God2_opt.exe game login screen.

## 狀態與取消

主畫面分別顯示捕捉後端、即時 Consumer、遊戲程序與分析狀態。從 Preparing、UAC、Worker、Backend、Consumer、Launcher／God2 搜尋、分析啟動到 DLL 附加階段都可以按「取消啟動」。取消會停止已啟動的本工具資源、保留原始證據並完成 Session；GUI 不會退出。

遊戲程序偵測不依賴 Consumer 成功。非管理員 GUI 若無法查詢已提升權限的 God2_opt.exe，會顯示 AccessDenied／正在驗證，並透過既有的管理員 Worker 回報完整路徑、x86、父 PID 與建立時間，而不是錯誤顯示「尚未啟動」。

## ETW Consumer READY

Consumer 只會在參數完成驗證、輸出 JSONL 開啟成功、`OpenTraceW` 成功且 callback 已配置後送出 READY。ETW session 尚未可見時，只對 `ERROR_WMI_INSTANCE_NOT_FOUND`、`ERROR_FILE_NOT_FOUND`、`ERROR_NOT_READY` 進行最多十秒、每 200ms 一次的有界重試，期間持續監看取消事件。

READY 後仍會持續核對 Consumer 的 PID、建立時間、映像路徑與存活狀態。若 Consumer 稍後退出，提升權限 Worker 會連同遊戲 PID 探測回報失敗；工具不會停止已 Active 的原始捕捉，而會轉為 `PostCaptureFallback`，保留 ETL／PCAPNG，停止時關閉中斷的即時分析 run，再自動執行離線分析。

若取消時 Windows 的 UAC 同意視窗仍開啟，Windows 不允許外部程序代替使用者關閉該安全桌面提示。工具會保持 GUI 與取消狀態；請先在 UAC 視窗選擇「否」，若顯示清理提示，再按一次停止完成有界清理。工具不會因 UAC 取消而退出。

## x86 DLL 增強擷取

此功能預設關閉。啟用時只處理完整路徑與 x86 架構驗證通過的 `God2_opt.exe`，短暫暫停主要執行緒、透過遠端執行緒載入 Winsock-only x86 DLL、等待 READY 後恢復。失敗不會阻止標準外部捕捉。
