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
```

- 默认：根据最新开奖自动生成下一期。
- `--issue`：指定完整期号。当前数据采用 `2026198` 这种格式，因此下一期是 `2026199`，不是裸 `199`。
- `--force`：覆盖已存在的预测文件。
- `--dry-run`：只校验数据和目标期号，不运行模型、不写文件。

结果先写入同目录临时文件，成功解析并校验后才替换正式 JSON；中断不会留下半个文件。

## 手机运行预测

1. 在 GitHub 打开仓库。
2. 点击 **Actions**。
3. 选择 **Run Prediction**。
4. 点击 **Run workflow**。
5. 可选填写完整 `issue`，或选择 `force` / `dry_run`。
6. 等待工作流完成，再打开 GitHub Pages 网站查看最新预测。

工作流有 `contents: write` 权限，只在预测文件确有变化时提交，提交信息为 `chore: generate prediction for issue <期号>`。并发组 `prediction-generation` 会避免两次点击同时写文件。

## GitHub Pages 配置

1. 新建 GitHub 仓库并将本目录初始化、提交和推送到 `main`（或 `master`）分支。
2. 在仓库 **Settings > Pages > Build and deployment** 中选择 **GitHub Actions**。
3. 首次手动运行 **Deploy Website**。

不需要账号密码、Token 或私钥。标准 GitHub Pages 部署和仓库内推送使用自动生成的 `GITHUB_TOKEN`，无需额外 Secrets。若组织策略禁用了 Actions 写权限，需要在 **Settings > Actions > General > Workflow permissions** 允许 Read and write permissions。

如果改用 Vercel、Netlify 或 Cloudflare Pages，请把发布目录设为 `site`，并让平台监听预测工作流推送的分支；本项目没有配置或需要任何平台私钥。

## 历史数据更新

云端运行不读取个人电脑，它读取仓库中的 `data/history.db`。新增开奖记录后，必须先把更新后的数据库快照提交到这个路径，再生成下一期预测。当前工作流没有启用自动爬取，以避免依赖不稳定的第三方数据源或 VPN。

## 定时运行

`.github/workflows/run-prediction.yml` 中预留了注释的 `schedule`。开奖时间确认后再取消注释并修改 cron；GitHub Actions cron 一律使用 **UTC**，不是北京时间。

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
