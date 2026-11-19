try {
  var t = localStorage.getItem('taskloom.theme')
  if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t)
  if (localStorage.getItem('taskloom.density') === 'compact') document.documentElement.setAttribute('data-density', 'compact')
} catch (e) {}
