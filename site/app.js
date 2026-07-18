const state = document.querySelector('#state');
const panel = document.querySelector('#prediction');
const refreshButton = document.querySelector('#refresh');

function text(id, value) {
  document.querySelector(`#${id}`).textContent = value;
}

function renderTokens(id, values, format = value => value) {
  const parent = document.querySelector(`#${id}`);
  parent.replaceChildren(...values.map(value => {
    const item = document.createElement('span');
    item.className = 'token';
    item.textContent = format(value);
    return item;
  }));
}

async function fetchJson(path) {
  const response = await fetch(`${path}?v=${Date.now()}`, { cache: 'no-store' });
  if (!response.ok) throw new Error(`${path} 返回 HTTP ${response.status}`);
  return response.json();
}

async function loadPrediction() {
  refreshButton.disabled = true;
  state.className = 'state';
  state.textContent = '正在读取最新预测...';
  state.hidden = false;
  panel.hidden = true;
  try {
    const latest = await fetchJson('data/predictions/latest.json');
    if (latest.status === 'generating') {
      state.textContent = '预测正在生成，请稍后刷新。';
      return;
    }
    if (latest.status === 'failed') throw new Error('生成失败，请查看 GitHub Actions 运行日志');
    if (!latest.prediction_file) throw new Error('latest.json 缺少 prediction_file');
    const result = await fetchJson(`data/predictions/${latest.prediction_file}`);
    if (result.status !== 'success' || !result.validation?.passed) {
      throw new Error('最新预测文件状态无效');
    }

    text('issue-title', `第 ${result.issue} 期预测`);
    text('generated-at', new Date(result.generated_at).toLocaleString('zh-CN'));
    text('status', '生成成功');
    text('data-range', `${result.data_range.start_issue} - ${result.data_range.end_issue}（${result.data_range.sample_count}期）`);
    text('model-version', result.model_version);
    renderTokens('zodiacs', result.prediction.zodiacs);
    renderTokens('numbers', result.prediction.numbers, value => String(value).padStart(2, '0'));
    renderTokens('recommendations', result.prediction.recommendations);
    state.hidden = true;
    panel.hidden = false;
  } catch (error) {
    state.className = 'state error';
    state.textContent = `加载失败：${error.message}`;
  } finally {
    refreshButton.disabled = false;
  }
}

refreshButton.addEventListener('click', loadPrediction);
loadPrediction();
