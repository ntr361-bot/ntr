# 六合分析软件

本项目包含原有 Windows 桌面分析程序、复用同一算法的云端预测运行器，以及由 GitHub Pages 发布的静态预测页。

## 项目结构

- `AIEngine.cs`、`ZodiacPredictEngineV2.cs`：原有预测算法。
- `history.db`：桌面程序本地数据库（不提交）。
- `data/history.db`：供 GitHub Actions 读取的历史数据快照（需要随开奖记录更新）。
- `PredictionAutomation.cs`：数据校验、期号计算、结果校验和安全 JSON 写入。
- `PredictionRunner/`：统一命令行入口。
- `site/`：网站及 `data/predictions/latest.json` 预测发布目录。
- `.github/workflows/run-prediction.yml`：手机手动触发预测。
- `.github/workflows/deploy-pages.yml`：GitHub Pages 部署。

## 本地运行

```powershell
dotnet run --project PredictionRunner -- --dry-run
dotnet run --project PredictionRunner --
dotnet run --project PredictionRunner -- --issue 2026199
dotnet run --project PredictionRunner -- --force
dotnet run --project PredictionRunner -- --refresh-data --refresh-only
```

- 默认：根据最新开奖自动生成下一期。
- `--issue`：指定完整期号。当前数据采用 `2026198` 这种格式，因此下一期是 `2026199`，不是裸 `199`。
- `--force`：覆盖已存在的预测文件。
- `--dry-run`：只校验数据和目标期号，不运行模型、不写文件。
- `--refresh-data`：预测前从开奖 API 抓取、严格校验并更新历史数据库。
- `--refresh-only`：只更新历史数据，不运行预测；通常与 `--refresh-data` 一起使用。

结果先写入同目录临时文件，成功解析并校验后才替换正式 JSON；中断不会留下半个文件。

## 手机运行预测

1. 在 GitHub 打开仓库。
2. 点击 **Actions**。
3. 选择 **Run Prediction**。
4. 点击 **Run workflow**。
5. 可选填写完整 `issue`，或选择 `force` / `dry_run`；`refresh_data` 默认保持开启。
6. 等待工作流完成，再打开 GitHub Pages 网站查看最新预测。

工作流有 `contents: write` 权限，只在预测文件确有变化时提交，提交信息为 `chore: generate prediction for issue <期号>`。并发组 `prediction-generation` 会避免两次点击同时写文件。

## GitHub Pages 配置

1. 新建 GitHub 仓库并将本目录初始化、提交和推送到 `main`（或 `master`）分支。
2. 在仓库 **Settings > Pages > Build and deployment** 中选择 **GitHub Actions**。
3. 首次手动运行 **Deploy Website**。

不需要账号密码、Token 或私钥。标准 GitHub Pages 部署和仓库内推送使用自动生成的 `GITHUB_TOKEN`，无需额外 Secrets。若组织策略禁用了 Actions 写权限，需要在 **Settings > Actions > General > Workflow permissions** 允许 Read and write permissions。

如果改用 Vercel、Netlify 或 Cloudflare Pages，请把发布目录设为 `site`，并让平台监听预测工作流推送的分支；本项目没有配置或需要任何平台私钥。

## 历史数据更新

云端运行不读取个人电脑。`Run Prediction` 默认先调用现有开奖 API，将通过校验的新记录事务写入 `data/history.db`，再生成下一期预测，并把数据库和预测 JSON 一起提交。API 无响应、数据为空、期号落后、号码重复、号码越界、生肖或日期无效时，工作流会停止，不会用损坏数据生成预测。

每次云端运行还会生成 `site/data/daily-records/<期号>.json`，包含 50/100/200/500 期 AI 生肖、特码规律验证、综合评分和集成模型的全部结果。任务会自动检查并补齐从首个已发布预测开始的遗漏期号；补生成旧期时只使用该期开奖前的数据，不会引入未来数据。手机网页优先读取该全量记录。

第三方数据源可能更换地址或限制 GitHub 服务器访问。遇到这种情况，Actions 日志会明确显示“开奖数据抓取失败”，历史数据库和网站预测保持原状。

## 定时运行

`.github/workflows/run-prediction.yml` 会在数据源通常完成更新后的北京时间每天 **21:45** 自动抓取，并在 **22:00**、**22:15** 重试。GitHub Actions 会检查最新开奖；有新数据时更新数据库、生成下一期并部署网站，没有新数据时安全跳过。配置中的 UTC cron 分别是 `45 13 * * *`、`0 14 * * *` 和 `15 14 * * *`，实际启动时间可能因 GitHub 队列稍有延迟。

电脑软件每次启动都会从云端补齐遗漏的开奖记录和每日预测档案，运行期间每 15 分钟再次检查。电脑关机不会影响云端生成；下次打开软件时会按照期号逐期同步，并以“期号 + 分析周期”去重，不会把同一期保存成多份。

每次 `Run Prediction` 成功结束后，`Deploy Website` 会自动发布它刚保存的档案。这个联动不依赖个人电脑，也不会因 GitHub 自动提交不触发普通 push 工作流而漏掉网页更新。

GitHub Pages 是静态网站，页面本身不会保存仓库写入密钥，因此不能安全地用“每次打开网页”直接触发写操作。定时检查保证电脑关机时仍持续更新；打开网站或点击刷新时会读取已经部署的最新一期。

## 常见问题

- **看不到 Run workflow**：工作流文件必须已存在于默认分支，并且仓库已启用 Actions。
- **推送被拒绝**：允许 Actions 的 Read and write permissions，并检查分支保护是否禁止机器人推送。
- **预测文件已存在**：正常情况下会跳过；确需重算时启用 `force`。
- **历史数据缺失或错误**：更新 `data/history.db`，日志会指出具体期号和字段。
- **网站仍显示旧数据**：确认 `latest.json` 已提交、Deploy Website 已成功；前端请求使用 `no-store` 和时间版本参数。
- **部署没有触发**：Pages 工作流只监听 `main` / `master` 的 `site/**` 变更；检查实际分支名。
- **Actions 成功但页面未更新**：在 Settings > Pages 确认来源为 GitHub Actions，并查看 Deploy Website 的 environment URL。
- **没有文件变化**：目标预测已存在或运行的是 dry-run；这是正常成功状态。

## 测试

```powershell
dotnet run --project Tests/六合分析软件.SmokeTests.csproj
```

测试数据写在构建输出下的隔离目录，不会修改正式 `history.db` 或 `site/data/predictions`。
